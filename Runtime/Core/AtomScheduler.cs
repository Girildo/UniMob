using System.Collections.Generic;
using System.Diagnostics;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;

namespace UniMob.Core
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class AtomScheduler : MonoBehaviour
    {
        public static readonly Stopwatch SyncTimer = new Stopwatch();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly CustomSampler ProfilerSampler = CustomSampler.Create("UniMob.Sync");
#endif
        private static Queue<AtomBase> _updatingCurrentFrame = new Queue<AtomBase>();
        private static Queue<AtomBase> _updatingNextFrame = new Queue<AtomBase>();

        private static AtomScheduler _current;
        private static bool _dirty;

        /// <summary>
        ///     Whether <see cref="Sync"/> has an atom to actualize. An atom that was deactivated,
        ///     disposed or brought up to date while it waited in the queue is not work, and does not count.
        /// </summary>
        public static bool HasPendingWork =>
            AnyWillActualize(_updatingCurrentFrame) || AnyWillActualize(_updatingNextFrame);

        /// <summary>
        ///     The atoms <see cref="Sync"/> will actualize, each listed once, in queue order. A new
        ///     snapshot on every read, meant for diagnostics.
        /// </summary>
        public static IReadOnlyList<AtomBase> PendingAtoms
        {
            get
            {
                var seen = new HashSet<AtomBase>();
                var pending = new List<AtomBase>();

                CollectPending(_updatingCurrentFrame, seen, pending);
                CollectPending(_updatingNextFrame, seen, pending);

                return pending;
            }
        }

        private void Update()
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;

            Sync();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            _current = null;

            // With domain reload disabled the queues outlive the previous session. An atom still
            // waiting in them is owed a Sync: invalidating it again will not queue it a second time.
            _dirty = HasPendingWork;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResumePendingWork()
        {
            if (_dirty)
            {
                EnsureRunning();
            }
        }

        internal static void Actualize(AtomBase atom)
        {
            _dirty = true;
            _updatingNextFrame.Enqueue(atom);

            EnsureRunning();
        }

        public static void Sync()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ProfilerSampler.Begin();
#endif
            SyncTimer.Restart();

            var toSwap = _updatingCurrentFrame;
            _updatingCurrentFrame = _updatingNextFrame;
            _updatingNextFrame = toSwap;

            while (_updatingCurrentFrame.Count > 0)
            {
                var atom = _updatingCurrentFrame.Dequeue();

                if (WillActualize(atom))
                {
                    atom.Actualize();
                }
            }

            SyncTimer.Stop();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ProfilerSampler.End();
#endif
        }

        private static void EnsureRunning()
        {
            if (ReferenceEquals(_current, null) && Application.isPlaying)
            {
                var go = new GameObject(nameof(AtomScheduler));
                _current = go.AddComponent<AtomScheduler>();
                DontDestroyOnLoad(_current);
            }
        }

        private static bool WillActualize(AtomBase atom)
        {
            return atom.options.Has(AtomOptions.Active) && atom.state != AtomState.Actual;
        }

        private static bool AnyWillActualize(Queue<AtomBase> queue)
        {
            foreach (var atom in queue)
            {
                if (WillActualize(atom))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectPending(
            Queue<AtomBase> queue,
            HashSet<AtomBase> seen,
            List<AtomBase> pending
        )
        {
            foreach (var atom in queue)
            {
                if (WillActualize(atom) && seen.Add(atom))
                {
                    pending.Add(atom);
                }
            }
        }
    }
}
