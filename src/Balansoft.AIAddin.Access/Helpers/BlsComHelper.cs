// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Reflection;

namespace Balansoft.AIAddin.Access.Helpers
{
    public static class BlsComHelper
    {
        public static dynamic GetVbe(dynamic accessApp)
        {
            // Access.Application has a VBE property returning the VBE object
            return accessApp.VBE;
        }

        public static dynamic GetCodeModule(dynamic vbe, string moduleName)
        {
            foreach (dynamic vbComp in vbe.VBProjects[0].VBComponents)
            {
                if (vbComp.Name == moduleName)
                {
                    return vbComp.CodeModule;
                }
            }
            return null;
        }

        public static void AddReference(dynamic vbe, string guid, int major, int minor)
        {
            var refs = vbe.ActiveVBProject.References;
            try
            {
                refs.AddFromGuid(guid, major, minor);
            }
            catch { }
        }
    }
}
