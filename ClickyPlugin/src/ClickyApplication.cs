namespace Loupedeck.ClickyPlugin;

using System;

public sealed class ClickyApplication : ClientApplication
{
    protected override string GetProcessName() => string.Empty;

    protected override string GetBundleName() => string.Empty;

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
