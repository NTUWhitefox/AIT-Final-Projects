// Batch-only replacement for Unity's native Cursor API: no window or OS pointer is controlled.
namespace AIT.Validation
{
    public static class DesktopLookPointer
    {
        public static UnityEngine.CursorLockMode lockState;
        public static bool visible = true;
    }
}
