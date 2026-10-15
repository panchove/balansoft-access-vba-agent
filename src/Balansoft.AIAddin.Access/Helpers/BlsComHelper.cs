// Copyright © 2026 Balansoft
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

        public static object? AddToolsMenuItem(object? vbe, string caption, string tag)
        {
            if (vbe == null) return null;
            try
            {
                dynamic v = vbe;
                var commandBars = v.CommandBars;
                var toolsBar = commandBars["Tools"];
                if (toolsBar == null) return null;

                object button = toolsBar.Controls.Add(Type: 1, Temporary: true);
                button.GetType().InvokeMember("Caption", BindingFlags.SetProperty, null, button, new object[] { caption });
                button.GetType().InvokeMember("Tag", BindingFlags.SetProperty, null, button, new object[] { tag });
                return button;
            }
            catch
            {
                return null;
            }
        }
    }
}