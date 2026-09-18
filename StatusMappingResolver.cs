using System;
using System.Collections.Generic;

namespace BackloggdStatus
{
    public class StatusMappingOperation
    {
        public string Status;         // "Playing"/"Backlog"/"Wishlist" (quick toggle) or a played sub-type / "unset-played-btn"
        public bool PlayedAlreadySet; // only meaningful for Played operations
    }

    public static class StatusMappingResolver
    {
        // Played sub-status > Playing > Backlog > Wishlist — most definitive signal wins.
        public static StatusMappingAction? ResolvePullSourceStatus(BackloggdGame bg)
        {
            if (bg.Played.HasValue)
                return (StatusMappingAction)Enum.Parse(typeof(StatusMappingAction), bg.Played.Value.ToString());
            if (bg.Playing)  return StatusMappingAction.Playing;
            if (bg.Backlog)  return StatusMappingAction.Backlog;
            if (bg.Wishlist) return StatusMappingAction.Wishlist;
            return null; // no active Backloggd status — nothing to pull
        }

        public static bool IsNoOpMapping(CompletionStatusMapping mapping) =>
            mapping.Playing == TriState.Unchanged && mapping.Backlog == TriState.Unchanged &&
            mapping.Wishlist == TriState.Unchanged && mapping.Played == PlayedTargetState.Unchanged;

        // Diffs a mapping's desired per-dimension state against a game's current state and
        // returns the ordered list of Backloggd operations actually needed to reach it —
        // dimensions left Unchanged, or already matching, contribute no operation.
        public static List<StatusMappingOperation> ComputeOperations(BackloggdGame bg, CompletionStatusMapping mapping)
        {
            var ops = new List<StatusMappingOperation>();

            if (mapping.Playing == TriState.On && !bg.Playing)
                ops.Add(new StatusMappingOperation { Status = "Playing" });
            if (mapping.Playing == TriState.Off && bg.Playing)
                ops.Add(new StatusMappingOperation { Status = "Playing" });

            if (mapping.Backlog == TriState.On && !bg.Backlog)
                ops.Add(new StatusMappingOperation { Status = "Backlog" });
            if (mapping.Backlog == TriState.Off && bg.Backlog)
                ops.Add(new StatusMappingOperation { Status = "Backlog" });

            if (mapping.Wishlist == TriState.On && !bg.Wishlist)
                ops.Add(new StatusMappingOperation { Status = "Wishlist" });
            if (mapping.Wishlist == TriState.Off && bg.Wishlist)
                ops.Add(new StatusMappingOperation { Status = "Wishlist" });

            switch (mapping.Played)
            {
                case PlayedTargetState.Unchanged:
                    break;
                case PlayedTargetState.Clear:
                    if (bg.Played.HasValue)
                        ops.Add(new StatusMappingOperation { Status = "unset-played-btn", PlayedAlreadySet = true });
                    break;
                default:
                    var target = (PlayedStatus)Enum.Parse(typeof(PlayedStatus), mapping.Played.ToString());
                    if (bg.Played != target)
                        ops.Add(new StatusMappingOperation { Status = target.ToString().ToLowerInvariant(), PlayedAlreadySet = bg.Played.HasValue });
                    break;
            }

            return ops;
        }
    }
}
