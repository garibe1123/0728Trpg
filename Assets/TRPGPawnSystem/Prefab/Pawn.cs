using UnityEngine;

namespace Trpg.Pawns
{
    public abstract class Pawn : MonoBehaviour
    {
        [SerializeField, Tooltip("씬 인스턴스를 구분하는 고유 ID")]
        private string _instanceId;

        public string InstanceId => _instanceId;
        public Vector2 WorldPosition => transform.position;
        public bool IsBound { get; private set; }

        public virtual void Bind()
        {
            IsBound = true;
        }

        public virtual void Unbind()
        {
            IsBound = false;
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_instanceId))
            {
                Debug.LogWarning($"[{name}] Pawn Instance Id가 비어 있습니다.", this);
            }
        }
#endif
    }
}
