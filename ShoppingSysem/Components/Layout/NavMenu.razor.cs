namespace ShoppingSysem.Components.Layout
{
    public partial class NavMenu
    {
        private bool collapsed = false;

        void ToggleSidebar()
        {
            collapsed = !collapsed;
        }
    }
}
