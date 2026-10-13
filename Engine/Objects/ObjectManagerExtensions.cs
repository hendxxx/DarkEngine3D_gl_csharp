using System;
using DarkEngine3D_gl_csharp.Engine.Helpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public static unsafe class ObjectManagerExtensions
    {
        public static void RemoveObject(this ObjectManager mgr, GltfObject obj)
        {
            if (mgr == null || obj == null) return;
            // Delegate to the manager's RemoveObject implementation
            try { mgr.RemoveObject(obj); }
            catch (Exception ex) { Console.WriteLine($"[ObjectManagerExtensions.RemoveObject] Exception: {ex.Message}"); }
        }
    }
}
