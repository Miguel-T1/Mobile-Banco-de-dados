using System.Collections.ObjectModel;
using TdeMauiBanco.Model;
using TdeMauiBanco.ModelView;

namespace TdeMauiBanco.View;

public partial class ClientesPage : ContentPage
{
    private readonly DatabaseService _database;
    private Cliente? _clienteSelecionado;

    public ClientesPage()
        : this(AppDatabase.Instance)
    {
    }

    public ClientesPage(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
        ClientesCollection.ItemsSource = Clientes;
    }

    public ObservableCollection<Cliente> Clientes { get; } = new();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CarregarClientes();
    }

    private async void OnSalvarClicked(object? sender, EventArgs e)
    {
        try
        {
            var cliente = _clienteSelecionado ?? new Cliente();
            cliente.Nome = NomeEntry.Text ?? string.Empty;
            cliente.Rg = RgEntry.Text ?? string.Empty;
            cliente.Telefone = TelefoneEntry.Text ?? string.Empty;

            var novo = cliente.CodigoCliente == 0;
            _database.SalvarCliente(cliente);

            await DisplayAlertAsync(
                "Sucesso",
                novo ? "Cliente cadastrado." : "Cliente atualizado.",
                "OK");
            LimparFormulario();
            CarregarClientes();
        }
        catch (BusinessRuleException erro)
        {
            await DisplayAlertAsync("Validação", erro.Message, "OK");
        }
        catch (Exception erro)
        {
            await DisplayAlertAsync("Erro", erro.Message, "OK");
        }
    }

    private void OnLimparClicked(object? sender, EventArgs e)
    {
        LimparFormulario();
    }

    private async void OnExcluirClicked(object? sender, EventArgs e)
    {
        if (_clienteSelecionado is null)
        {
            await DisplayAlertAsync("Atenção", "Selecione um cliente para excluir.", "OK");
            return;
        }

        var confirmar = await DisplayAlertAsync(
            "Excluir cliente",
            $"Deseja excluir {_clienteSelecionado.Nome}?",
            "Excluir",
            "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            _database.ExcluirCliente(_clienteSelecionado.CodigoCliente);
            await DisplayAlertAsync("Sucesso", "Cliente excluído.", "OK");
            LimparFormulario();
            CarregarClientes();
        }
        catch (BusinessRuleException erro)
        {
            await DisplayAlertAsync("Não foi possível excluir", erro.Message, "OK");
        }
        catch (Exception erro)
        {
            await DisplayAlertAsync("Erro", erro.Message, "OK");
        }
    }

    private void OnClienteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _clienteSelecionado = e.CurrentSelection.FirstOrDefault() as Cliente;
        if (_clienteSelecionado is null)
        {
            return;
        }

        NomeEntry.Text = _clienteSelecionado.Nome;
        RgEntry.Text = _clienteSelecionado.Rg;
        TelefoneEntry.Text = _clienteSelecionado.Telefone;
        TituloFormulario.Text = $"Editando cliente #{_clienteSelecionado.CodigoCliente}";
        SalvarButton.Text = "Atualizar cliente";
    }

    private void CarregarClientes()
    {
        Clientes.Clear();
        foreach (var cliente in _database.ObterClientes())
        {
            Clientes.Add(cliente);
        }
    }

    private void LimparFormulario()
    {
        _clienteSelecionado = null;
        ClientesCollection.SelectedItem = null;
        NomeEntry.Text = string.Empty;
        RgEntry.Text = string.Empty;
        TelefoneEntry.Text = string.Empty;
        TituloFormulario.Text = "Novo cliente";
        SalvarButton.Text = "Cadastrar cliente";
    }
}
