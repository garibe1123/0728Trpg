using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Trpg.Save
{
    public readonly struct SaveSlotInfo
    {
        public SaveSlotInfo(
            string saveId,
            string saveName,
            DateTime savedAtUtc)
        {
            SaveId = saveId;
            SaveName = saveName;
            SavedAtUtc = savedAtUtc;
        }

        public string SaveId { get; }
        public string SaveName { get; }
        public DateTime SavedAtUtc { get; }
    }

    public sealed class CampaignSaveService
    {
        public const int CurrentSchemaVersion = 2;
        private const int MaximumSaveNameLength = 40;
        private const string SaveExtension = ".json";

        private readonly string _saveDirectory;
        private readonly UTF8Encoding _utf8WithoutBom =
            new UTF8Encoding(false);

        public CampaignSaveService(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException(
                    "저장 루트 경로가 비어 있습니다.",
                    nameof(persistentDataPath));
            }

            _saveDirectory = Path.Combine(
                persistentDataPath,
                "Campaigns",
                "default");
        }

        public IReadOnlyList<SaveSlotInfo> ListSlots()
        {
            var result = new List<SaveSlotInfo>();
            if (!Directory.Exists(_saveDirectory))
                return result;

            var files = Directory.GetFiles(
                _saveDirectory,
                "*" + SaveExtension,
                SearchOption.TopDirectoryOnly);
            for (var index = 0; index < files.Length; index++)
            {
                if (!TryReadSnapshot(
                        files[index],
                        out var snapshot,
                        out _))
                {
                    continue;
                }

                if (!TryParseSaveId(snapshot.SaveId, out _) ||
                    string.IsNullOrWhiteSpace(snapshot.SaveName) ||
                    !DateTime.TryParse(
                        snapshot.SavedAtUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var savedAt))
                {
                    continue;
                }

                result.Add(
                    new SaveSlotInfo(
                        snapshot.SaveId,
                        snapshot.SaveName,
                        savedAt.ToUniversalTime()));
            }

            result.Sort(
                (left, right) =>
                    right.SavedAtUtc.CompareTo(left.SavedAtUtc));
            return result;
        }

        public bool TrySaveNew(
            string saveName,
            CampaignSnapshot snapshot,
            out SaveSlotInfo slot,
            out string error)
        {
            slot = default(SaveSlotInfo);
            error = string.Empty;
            var normalizedName = NormalizeSaveName(saveName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                error = "저장 이름을 입력해 주세요.";
                return false;
            }

            if (snapshot == null)
            {
                error = "저장할 캠페인 데이터가 없습니다.";
                return false;
            }

            var id = Guid.NewGuid().ToString("N");
            var savedAt = DateTime.UtcNow;
            snapshot.SchemaVersion = CurrentSchemaVersion;
            snapshot.SaveId = id;
            snapshot.SaveName = normalizedName;
            snapshot.SavedAtUtc = savedAt.ToString(
                "O",
                CultureInfo.InvariantCulture);

            var finalPath = GetPath(id);
            var temporaryPath = finalPath + ".tmp";
            try
            {
                Directory.CreateDirectory(_saveDirectory);
                var json = JsonUtility.ToJson(snapshot, true);
                File.WriteAllText(
                    temporaryPath,
                    json,
                    _utf8WithoutBom);
                File.Move(temporaryPath, finalPath);
                slot = new SaveSlotInfo(id, normalizedName, savedAt);
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteFile(temporaryPath);
                error = "저장 파일을 쓰지 못했습니다: " +
                        exception.Message;
                return false;
            }
        }

        public bool TryLoad(
            string saveId,
            out CampaignSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (!TryParseSaveId(saveId, out var normalizedId))
            {
                error = "유효하지 않은 저장 ID입니다.";
                return false;
            }

            if (!TryReadSnapshot(
                    GetPath(normalizedId),
                    out snapshot,
                    out error))
            {
                return false;
            }

            if (snapshot.SchemaVersion > CurrentSchemaVersion)
            {
                error =
                    "현재 코드보다 새로운 버전의 저장 파일입니다.";
                snapshot = null;
                return false;
            }

            if (snapshot.SchemaVersion < 1)
            {
                error = "지원하지 않는 저장 파일 버전입니다.";
                snapshot = null;
                return false;
            }

            return true;
        }

        public bool TryDelete(string saveId, out string error)
        {
            error = string.Empty;
            if (!TryParseSaveId(saveId, out var normalizedId))
            {
                error = "유효하지 않은 저장 ID입니다.";
                return false;
            }

            var path = GetPath(normalizedId);
            if (!File.Exists(path))
            {
                error = "삭제할 저장 파일이 없습니다.";
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception exception)
            {
                error = "저장 파일을 삭제하지 못했습니다: " +
                        exception.Message;
                return false;
            }
        }

        public bool TryResetAll(
            out int deletedCount,
            out string error)
        {
            deletedCount = 0;
            error = string.Empty;
            if (!Directory.Exists(_saveDirectory))
                return true;

            string[] files;
            try
            {
                files = Directory.GetFiles(
                    _saveDirectory,
                    "*" + SaveExtension,
                    SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception)
            {
                error = "저장 기록을 확인하지 못했습니다: " +
                        exception.Message;
                return false;
            }

            var failedCount = 0;
            for (var index = 0; index < files.Length; index++)
            {
                var saveId =
                    Path.GetFileNameWithoutExtension(files[index]);
                if (!TryParseSaveId(saveId, out var normalizedId) ||
                    !string.Equals(
                        saveId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(files[index]);
                    deletedCount++;
                }
                catch (Exception)
                {
                    failedCount++;
                }
            }

            if (failedCount <= 0)
                return true;

            error =
                $"{deletedCount}개 삭제, {failedCount}개 삭제 실패했습니다.";
            return false;
        }

        private bool TryReadSnapshot(
            string path,
            out CampaignSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (!File.Exists(path))
            {
                error = "저장 파일이 없습니다.";
                return false;
            }

            try
            {
                var json = File.ReadAllText(path, _utf8WithoutBom);
                snapshot =
                    JsonUtility.FromJson<CampaignSnapshot>(json);
                if (snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.SaveId))
                {
                    error = "저장 파일 형식이 올바르지 않습니다.";
                    snapshot = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "저장 파일을 읽지 못했습니다: " +
                        exception.Message;
                return false;
            }
        }

        private string GetPath(string saveId)
        {
            return Path.Combine(
                _saveDirectory,
                saveId + SaveExtension);
        }

        private static string NormalizeSaveName(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
            return normalized.Length <= MaximumSaveNameLength
                ? normalized
                : normalized.Substring(0, MaximumSaveNameLength);
        }

        private static bool TryParseSaveId(
            string saveId,
            out string normalizedId)
        {
            normalizedId = string.Empty;
            if (!Guid.TryParseExact(
                    saveId,
                    "N",
                    out var parsed))
            {
                return false;
            }

            normalizedId = parsed.ToString("N");
            return true;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 임시 파일 정리 실패가 원래 저장 오류를 가리지 않게 한다.
            }
        }
    }
}
