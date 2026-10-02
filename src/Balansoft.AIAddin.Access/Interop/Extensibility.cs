// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Runtime.InteropServices;

namespace Balansoft.AIAddin.Access.Interop
{
    [ComImport, Guid("00000000-0000-0000-0000-000000000000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDTExtensibility2
    {
        void OnConnection(object Application, ext_ConnectMode ConnectMode, object AddInInst, ref Array custom);
        void OnDisconnection(ext_DisconnectMode RemoveMode, ref Array custom);
        void OnAddInsUpdate(ref Array custom);
        void OnStartupComplete(ref Array custom);
        void OnBeginShutdown(ref Array custom);
    }

    public enum ext_ConnectMode
    {
        ext_cm_AfterStartup = 0,
        ext_cm_Startup = 1,
        ext_cm_CommandLine = 2,
        ext_cm_External = 3,
        ext_cm_UISetup = 4,
        ext_cm_UISetupComplete = 5
    }

    public enum ext_DisconnectMode
    {
        ext_dm_HostShutdown = 0,
        ext_dm_UserClosed = 1,
        ext_dm_Suspended = 2,
        ext_dm_UISetupComplete = 3
    }
}
