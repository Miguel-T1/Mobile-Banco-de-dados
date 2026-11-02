namespace TdeMauiBanco.View;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnClientesClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(ClientesPage));
    }

    private async void OnProdutosClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(ProdutosPage));
    }

    private async void OnComprasClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(ComprasPage));
    }
}
