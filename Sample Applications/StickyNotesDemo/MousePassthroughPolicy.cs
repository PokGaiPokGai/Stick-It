using System;

namespace StickyNotesDemo;

internal enum CursorVisibility { Hidden, Visible, Suppressed }
internal enum CursorConstraint { Free, Confined, Locked }
internal enum CursorSignal { Sample, Hidden, Shown }

internal readonly record struct PointerSnapshot(
    IntPtr Foreground, uint ForegroundThread, bool OwnForeground,
    CursorVisibility Visibility, CursorConstraint Constraint,
    bool Fullscreen, bool OverOwnWindow);

internal sealed class MousePassthroughPolicy
{
    private IntPtr foreground;
    private CursorConstraint previousConstraint;
    private bool releasedConstraint;
    internal bool Passthrough { get; private set; }

    internal bool Update(PointerSnapshot state, CursorSignal signal = CursorSignal.Sample)
    {
        if (state.Foreground == IntPtr.Zero || state.OwnForeground)
        {
            Reset();
            return false;
        }
        if (state.Foreground != foreground)
        {
            Reset();
            foreground = state.Foreground;
        }
        if (state.Visibility == CursorVisibility.Suppressed)
        {
            Reset(); // Touch/pen suppression is not a game hiding its pointer.
            return false;
        }

        // A software-cursor menu can release the confinement while keeping the
        // Windows cursor hidden. Remember that release until a new capture/hide.
        if (previousConstraint != CursorConstraint.Free && state.Constraint == CursorConstraint.Free)
            releasedConstraint = true;
        // An asynchronous hide event can already be obsolete by the time it
        // arrives. Outside our windows, the fresh cursor sample wins.
        var foregroundHidCursor = signal == CursorSignal.Hidden &&
            (state.Visibility == CursorVisibility.Hidden || state.OverOwnWindow);
        if (state.Constraint != CursorConstraint.Free || foregroundHidCursor)
            releasedConstraint = false;
        previousConstraint = state.Constraint;

        if (state.Constraint == CursorConstraint.Locked)
            return Passthrough = true;
        if (releasedConstraint && state.Constraint == CursorConstraint.Free)
            return Passthrough = false;

        // An interactive note can supply its own visible cursor over a game.
        // A previous note cursor can also remain until the next mouse movement.
        // Do not let that stale image undo protection; wait for the game's event
        // or a sample outside our windows instead.
        if (signal == CursorSignal.Sample && state.OverOwnWindow)
            return Passthrough;

        var visibility = foregroundHidCursor ? CursorVisibility.Hidden : state.Visibility;
        return Passthrough = visibility == CursorVisibility.Hidden &&
            (state.Constraint == CursorConstraint.Confined || state.Fullscreen && !releasedConstraint);
    }

    internal void Reset()
    {
        foreground = IntPtr.Zero;
        previousConstraint = CursorConstraint.Free;
        releasedConstraint = false;
        Passthrough = false;
    }
}
