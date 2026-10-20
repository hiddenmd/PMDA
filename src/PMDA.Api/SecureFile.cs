using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PMDA.Api;

/// <summary>
/// Files holding secrets or accounts (database registry, users): written atomically, and on Windows readable only by
/// Administrators, SYSTEM and the account the API runs as.
/// </summary>
public static class SecureFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        if (OperatingSystem.IsWindows())
            Restrict(temp);
        File.Move(temp, path, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    private static void Restrict(string file)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     WindowsIdentity.GetCurrent().User!,
                 })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(file).SetAccessControl(security);
    }
}
