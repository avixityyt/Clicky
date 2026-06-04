namespace Loupedeck.ClickyPlugin;

using System;

public enum GlobalMouseButton
{
    Left,
    Right,
    Middle,
}

public sealed class GlobalMouseClickEventArgs : EventArgs
{
    public GlobalMouseClickEventArgs(GlobalMouseButton button) => this.Button = button;

    public GlobalMouseButton Button { get; }
}
