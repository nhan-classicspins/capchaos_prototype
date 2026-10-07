using System;
using System.Collections.Generic;

namespace Game.Application
{
    /// <summary>What one resync of the level config did (<see cref="LevelCatalog.Override"/>).</summary>
    public sealed class LevelSyncReport
    {
        /// <summary>Why nothing was fetched (sync turned off, no sheet URL, the network failed); null when it ran.</summary>
        public string Skipped { get; }
        /// <summary>Level ids that took a new text.</summary>
        public IReadOnlyList<string> Changed { get; }
        /// <summary>Conveyor ids that took a new layout (the levels on them were re-laid onto it).</summary>
        public IReadOnlyList<string> ChangedConveyors { get; }
        /// <summary>Levels and conveyors the source had with the same text they already have.</summary>
        public int Unchanged { get; }
        /// <summary>One line per refused level or conveyor (it keeps what it had), or a tab that could not be read.</summary>
        public IReadOnlyList<string> Problems { get; }

        public bool Ran => Skipped == null;

        public LevelSyncReport(IReadOnlyList<string> changed, int unchanged, IReadOnlyList<string> problems,
            IReadOnlyList<string> changedConveyors = null)
        {
            Changed = changed ?? Array.Empty<string>();
            ChangedConveyors = changedConveyors ?? Array.Empty<string>();
            Unchanged = unchanged;
            Problems = problems ?? Array.Empty<string>();
        }

        private LevelSyncReport(string skipped) : this(null, 0, null) => Skipped = skipped;

        public static LevelSyncReport NotRun(string reason) => new LevelSyncReport(reason ?? "skipped");

        /// <summary>This report with <paramref name="more"/> problems added (a tab that could not be read).</summary>
        public LevelSyncReport With(IReadOnlyList<string> more)
        {
            if (!Ran || more == null || more.Count == 0) return this;
            var all = new List<string>(Problems);
            all.AddRange(more);
            return new LevelSyncReport(Changed, Unchanged, all, ChangedConveyors);
        }

        public override string ToString() => Ran
            ? $"{Changed.Count} level(s) and {ChangedConveyors.Count} conveyor(s) changed, {Unchanged} unchanged, {Problems.Count} problem(s)"
            : "skipped: " + Skipped;
    }
}
