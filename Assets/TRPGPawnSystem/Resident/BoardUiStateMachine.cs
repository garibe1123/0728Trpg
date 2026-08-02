using System;

namespace Trpg.Pawns
{
    public enum BoardMode
    {
        Idle,
        Walk,
        Sheet
    }

    public enum SheetTab
    {
        None,
        Stats,
        Bag,
        Profile
    }

    public enum SheetDetail
    {
        None,
        SkillList,
        RollRoulette
    }

    public readonly struct BoardUiState : IEquatable<BoardUiState>
    {
        public BoardUiState(
            BoardMode mode,
            SheetTab tab = SheetTab.None,
            SheetDetail detail = SheetDetail.None,
            bool popover = false,
            bool modal = false)
        {
            Mode = mode;
            Tab = mode == BoardMode.Sheet ? tab : SheetTab.None;
            Detail = mode == BoardMode.Sheet && tab == SheetTab.Stats
                ? detail
                : SheetDetail.None;
            Popover = popover;
            Modal = modal;
        }

        public BoardMode Mode { get; }
        public SheetTab Tab { get; }
        public SheetDetail Detail { get; }
        public bool Popover { get; }
        public bool Modal { get; }
        public bool HasSheet => Mode == BoardMode.Sheet;
        public bool HasDetail =>
            Mode == BoardMode.Sheet &&
            Tab == SheetTab.Stats &&
            Detail != SheetDetail.None;

        public BoardUiState WithPopover(bool value)
        {
            return new BoardUiState(
                Mode,
                Tab,
                Detail,
                value,
                Modal);
        }

        public BoardUiState WithModal(bool value)
        {
            return new BoardUiState(
                Mode,
                Tab,
                Detail,
                Popover,
                value);
        }

        public bool Equals(BoardUiState other)
        {
            return Mode == other.Mode &&
                   Tab == other.Tab &&
                   Detail == other.Detail &&
                   Popover == other.Popover &&
                   Modal == other.Modal;
        }

        public override bool Equals(object obj)
        {
            return obj is BoardUiState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Mode;
                hash = hash * 397 ^ (int)Tab;
                hash = hash * 397 ^ (int)Detail;
                hash = hash * 397 ^ Popover.GetHashCode();
                hash = hash * 397 ^ Modal.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            BoardUiState left,
            BoardUiState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            BoardUiState left,
            BoardUiState right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Unity API에 의존하지 않는 보드 UI 상태 머신입니다.
    /// Popover와 Modal은 일곱 개 코어 상태와 직교합니다.
    /// </summary>
    public sealed class BoardUiStateMachine
    {
        private BoardUiState _state = new BoardUiState(BoardMode.Idle);

        public BoardUiState State => _state;
        public event Action<BoardUiState, BoardUiState> Changed;

        public BoardUiState ClickWalk()
        {
            var next = _state.Mode == BoardMode.Walk
                ? new BoardUiState(
                    BoardMode.Idle,
                    popover: _state.Popover,
                    modal: _state.Modal)
                : new BoardUiState(
                    BoardMode.Walk,
                    popover: _state.Popover,
                    modal: _state.Modal);
            return Commit(next);
        }

        public BoardUiState ClickTab(SheetTab tab)
        {
            if (tab == SheetTab.None)
            {
                return Commit(new BoardUiState(
                    BoardMode.Idle,
                    popover: _state.Popover,
                    modal: _state.Modal));
            }

            var sameTab =
                _state.Mode == BoardMode.Sheet &&
                _state.Tab == tab;
            return Commit(sameTab
                ? new BoardUiState(
                    BoardMode.Idle,
                    popover: _state.Popover,
                    modal: _state.Modal)
                : new BoardUiState(
                    BoardMode.Sheet,
                    tab,
                    SheetDetail.None,
                    _state.Popover,
                    _state.Modal));
        }

        public BoardUiState FocusSheet(
            SheetTab tab,
            SheetDetail detail = SheetDetail.None)
        {
            return Commit(new BoardUiState(
                BoardMode.Sheet,
                tab,
                detail,
                _state.Popover,
                _state.Modal));
        }

        public BoardUiState ClickSkillList()
        {
            var same =
                _state.Mode == BoardMode.Sheet &&
                _state.Tab == SheetTab.Stats &&
                _state.Detail == SheetDetail.SkillList;
            return Commit(new BoardUiState(
                BoardMode.Sheet,
                SheetTab.Stats,
                same ? SheetDetail.None : SheetDetail.SkillList,
                _state.Popover,
                _state.Modal));
        }

        public BoardUiState ClickCheckRoll()
        {
            return Commit(new BoardUiState(
                BoardMode.Sheet,
                SheetTab.Stats,
                SheetDetail.RollRoulette,
                _state.Popover,
                _state.Modal));
        }

        public BoardUiState SelectRollSource()
        {
            return ClickCheckRoll();
        }

        public BoardUiState TogglePopover()
        {
            return Commit(_state.WithPopover(!_state.Popover));
        }

        public BoardUiState SetModal(bool value)
        {
            return Commit(_state.WithModal(value));
        }

        public BoardUiState Escape()
        {
            if (_state.Modal)
                return Commit(_state.WithModal(false));
            if (_state.Popover)
                return Commit(_state.WithPopover(false));
            if (_state.Detail != SheetDetail.None)
            {
                return Commit(new BoardUiState(
                    BoardMode.Sheet,
                    SheetTab.Stats));
            }
            if (_state.Mode == BoardMode.Sheet ||
                _state.Mode == BoardMode.Walk)
            {
                return Commit(new BoardUiState(BoardMode.Idle));
            }

            return _state;
        }

        public BoardUiState ForceIdle()
        {
            return Commit(new BoardUiState(BoardMode.Idle));
        }

        public BoardUiState Force(BoardUiState state)
        {
            return Commit(state);
        }

        private BoardUiState Commit(BoardUiState next)
        {
            if (_state == next)
                return _state;

            var previous = _state;
            _state = next;
            Changed?.Invoke(previous, next);
            return _state;
        }
    }
}

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
namespace Trpg.Pawns.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;

    internal sealed class BoardUiStateMachineTests
    {
        [Test]
        public void ReachableCoreStates_AreExactlySeven()
        {
            var discovered = new HashSet<BoardUiState>();
            var queue = new Queue<BoardUiState>();
            var initial = new BoardUiState(BoardMode.Idle);
            discovered.Add(initial);
            queue.Enqueue(initial);

            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                foreach (var next in Enumerate(state))
                {
                    var core = new BoardUiState(
                        next.Mode,
                        next.Tab,
                        next.Detail);
                    if (discovered.Add(core))
                        queue.Enqueue(core);
                }
            }

            Assert.AreEqual(7, discovered.Count);
        }

        [Test]
        public void Escape_ConvergesToIdle()
        {
            var states = new[]
            {
                new BoardUiState(BoardMode.Idle),
                new BoardUiState(BoardMode.Walk),
                new BoardUiState(BoardMode.Sheet, SheetTab.Stats),
                new BoardUiState(BoardMode.Sheet, SheetTab.Bag),
                new BoardUiState(BoardMode.Sheet, SheetTab.Profile),
                new BoardUiState(
                    BoardMode.Sheet,
                    SheetTab.Stats,
                    SheetDetail.SkillList,
                    true,
                    true),
                new BoardUiState(
                    BoardMode.Sheet,
                    SheetTab.Stats,
                    SheetDetail.RollRoulette,
                    true,
                    true)
            };

            foreach (var state in states)
            {
                var machine = new BoardUiStateMachine();
                machine.Force(state);
                for (var index = 0; index < 8; index++)
                    machine.Escape();
                Assert.AreEqual(BoardMode.Idle, machine.State.Mode);
                Assert.AreEqual(SheetTab.None, machine.State.Tab);
                Assert.AreEqual(SheetDetail.None, machine.State.Detail);
                Assert.IsFalse(machine.State.Popover);
                Assert.IsFalse(machine.State.Modal);
            }
        }

        [Test]
        public void Popover_DoesNotChangeCoreState()
        {
            var machine = new BoardUiStateMachine();
            machine.ClickTab(SheetTab.Bag);
            var before = machine.State;
            machine.TogglePopover();
            var after = machine.State;
            Assert.AreEqual(before.Mode, after.Mode);
            Assert.AreEqual(before.Tab, after.Tab);
            Assert.AreEqual(before.Detail, after.Detail);
        }

        private static IEnumerable<BoardUiState> Enumerate(
            BoardUiState state)
        {
            var commands = new System.Action<BoardUiStateMachine>[]
            {
                machine => machine.ClickWalk(),
                machine => machine.ClickTab(SheetTab.Stats),
                machine => machine.ClickTab(SheetTab.Bag),
                machine => machine.ClickTab(SheetTab.Profile),
                machine => machine.ClickSkillList(),
                machine => machine.ClickCheckRoll(),
                machine => machine.Escape()
            };

            foreach (var command in commands)
            {
                var machine = new BoardUiStateMachine();
                machine.Force(state);
                command(machine);
                yield return machine.State;
            }
        }
    }
}
#endif
