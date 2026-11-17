using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Stash
{
    // Once releases are code-signed, an update must carry a valid Authenticode signature from the
    // same publisher as the running copy. Unsigned builds skip this check, so the SHA-256 sidecar
    // remains the integrity check until a signing certificate is used (see build.bat).
    internal static class AuthenticodeVerifier
    {
        internal static string GetPublisher(string path)
        {
            try
            {
                using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    return certificate.Subject;
            }
            catch (CryptographicException) { return null; }
        }

        internal static void VerifyUpdate(string downloadedPath, string runningPath)
        {
            string expectedPublisher = GetPublisher(runningPath);
            if (expectedPublisher == null) return;
            string actualPublisher = GetPublisher(downloadedPath);
            if (actualPublisher == null)
                throw new InvalidDataException("The update is not code-signed, but this copy of Stash is. The update was rejected.");
            if (!string.Equals(expectedPublisher, actualPublisher, StringComparison.Ordinal))
                throw new InvalidDataException("The update is signed by a different publisher (" + actualPublisher + "). The update was rejected.");
            int result = Verify(downloadedPath);
            if (result != 0)
                throw new InvalidDataException("The update's code signature is not valid (0x" + result.ToString("X8") + "). The update was rejected.");
        }

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private static int Verify(string path)
        {
            WinTrustFileInfo file = new WinTrustFileInfo(path);
            IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(file, fileInfo, false);
                WinTrustData data = new WinTrustData(fileInfo);
                Guid action = GenericVerifyV2;
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }
            finally
            {
                Marshal.DestroyStructure(fileInfo, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(fileInfo);
            }
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, [In] ref Guid action, [In] ref WinTrustData data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;

            public WinTrustFileInfo(string path)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                FilePath = path;
                FileHandle = IntPtr.Zero;
                KnownSubject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;

            public WinTrustData(IntPtr fileInfo)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = 2; // WTD_UI_NONE
                RevocationChecks = 0; // WTD_REVOKE_NONE
                UnionChoice = 1; // WTD_CHOICE_FILE
                FileInfo = fileInfo;
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = 0x10; // WTD_SAFER_FLAG
                UiContext = 0;
                SignatureSettings = IntPtr.Zero;
            }
        }
    }
}
