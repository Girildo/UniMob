using System.Linq;
using NUnit.Framework;
using UniMob.Core;

namespace UniMob.Tests
{
    public class AtomSchedulerTests
    {
        private LifetimeController _lifetimeController;

        private Lifetime Lifetime => _lifetimeController.Lifetime;

        [SetUp]
        public void SetUp()
        {
            _lifetimeController = new LifetimeController();

            // Edit mode has no scheduler object, so whatever an earlier test queued is still queued.
            AtomScheduler.Sync();
        }

        [TearDown]
        public void TearDown()
        {
            _lifetimeController.Dispose();
        }

        [Test]
        public void AQueuedReaction_IsPendingUntilSynced()
        {
            var source = Atom.Value(Lifetime, 0);
            var runs = 0;
            Atom.Reaction(
                Lifetime,
                () =>
                {
                    var unused = source.Value;
                    runs++;
                },
                debugName: "follower"
            );

            source.Value = 1;

            Assert.That(AtomScheduler.HasPendingWork, Is.True, "an invalidated reaction is work");
            Assert.That(Names(), Is.EqualTo(new[] { "follower" }));

            AtomScheduler.Sync();

            Assert.That(runs, Is.EqualTo(2), "the sync is what re-ran it");
            Assert.That(AtomScheduler.HasPendingWork, Is.False, "nothing is left once it ran");
            Assert.That(Names(), Is.Empty);
        }

        [Test]
        public void AReactionDisposedWhileQueued_IsNotPending()
        {
            var source = Atom.Value(Lifetime, 0);
            var reactionLifetime = new LifetimeController();
            Atom.Reaction(
                reactionLifetime.Lifetime,
                () =>
                {
                    var unused = source.Value;
                },
                debugName: "disposed follower"
            );

            source.Value = 1;
            reactionLifetime.Dispose();

            Assert.That(
                AtomScheduler.HasPendingWork,
                Is.False,
                "Sync skips a disposed atom, so its queue entry is not work; still pending: "
                    + string.Join(", ", Names())
            );
            Assert.That(Names(), Is.Empty);
        }

        [Test]
        public void AnAtomReadUpToDateWhileQueued_IsNotPending()
        {
            var source = Atom.Value(Lifetime, 1);
            var target = Atom.Computed(
                Lifetime,
                () => source.Value * 2,
                keepAlive: true,
                debugName: "kept alive"
            );
            Assert.That(target.Value, Is.EqualTo(2));

            source.Value = 2;
            Assume.That(
                AtomScheduler.HasPendingWork,
                Is.True,
                "a kept-alive computed is queued when invalidated"
            );

            Assert.That(target.Value, Is.EqualTo(4), "the read actualizes it ahead of the sync");
            Assert.That(
                AtomScheduler.HasPendingWork,
                Is.False,
                "Sync skips an actual atom, so its queue entry is not work; still pending: "
                    + string.Join(", ", Names())
            );
        }

        [Test]
        public void AnAtomQueuedTwice_IsNamedOnce()
        {
            var source = Atom.Value(Lifetime, 1);
            var target = Atom.Computed(
                Lifetime,
                () => source.Value * 2,
                keepAlive: true,
                debugName: "requeued"
            );
            Assert.That(target.Value, Is.EqualTo(2));

            source.Value = 2;
            Assert.That(target.Value, Is.EqualTo(4));
            source.Value = 3;

            Assert.That(Names(), Is.EqualTo(new[] { "requeued" }));
        }

        private static string[] Names()
        {
            return AtomScheduler.PendingAtoms.Select(atom => atom.ToString()).ToArray();
        }
    }
}
