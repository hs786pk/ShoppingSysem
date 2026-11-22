using Microsoft.AspNetCore.Components.Routing;

namespace ShoppingSysem.Components.Layout
{
    public partial class PageLoadingBar
    {
        private bool isLoading = false;
        private CancellationTokenSource? cts;

        protected override void OnInitialized()
        {
            NavManager.LocationChanged += OnLocationChanged;
        }

        private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            isLoading = true;
            StateHasChanged();

            try
            {
                // Brief delay so the bar is visible even on very fast page changes
                await Task.Delay(300, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!token.IsCancellationRequested)
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        public void Dispose()
        {
            NavManager.LocationChanged -= OnLocationChanged;
        }
    }
}
