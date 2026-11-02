using System.Collections.ObjectModel;
using System.Globalization;
using TdeMauiBanco.Model;
using TdeMauiBanco.ModelView;

namespace TdeMauiBanco.View;

public partial class ComprasPage : ContentPage
{
    private readonly DatabaseService _database;
    private Compra? _compraSelecionada;
    private ItemCompra? _itemSelecionado;

    public ComprasPage()
        : this(AppDatabase.Instance)
    {
    }

    public ComprasPage(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
        ClientePicker.ItemsSource = Clientes;
        ProdutoPicker.ItemsSource = Produtos;
        ItensCollection.ItemsSource = Itens;
        ComprasCollection.ItemsSource = Compras;
        DataCompraPicker.Date = DateTime.Today;
        AtualizarTotal();
    }

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Produto> Produtos { get; } = new();
    public ObservableCollection<ItemCompra> Itens { get; } = new();
    public ObservableCollection<Compra> Compras { get; } = new();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CarregarDados();
    }

    private async void OnSalvarItemClicked(object? sender, EventArgs e)
    {
        if (ProdutoPicker.SelectedItem is not Produto produto)
        {
            await DisplayAlertAsync("Validação", "Selecione um produto.", "OK");
            return;
        }

        if (!int.TryParse(
                QuantidadeEntry.Text?.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var quantidade)
            || quantidade <= 0)
        {
            await DisplayAlertAsync("Validação", "Informe uma quantidade inteira maior que zero.", "OK");
            return;
        }

        if (Itens.Any(item =>
                item != _itemSelecionado && item.CodigoProduto == produto.CodigoProduto))
        {
            await DisplayAlertAsync(
                "Validação",
                "Esse produto já está na compra. Selecione a linha existente para editar a quantidade.",
                "OK");
            return;
        }

        if (_itemSelecionado is null)
        {
            Itens.Add(new ItemCompra
            {
                CodigoProduto = produto.CodigoProduto,
                CodigoCompra = _compraSelecionada?.CodigoCompra ?? 0,
                Quantidade = quantidade,
                ValorParcial = Math.Round(produto.Preco * quantidade, 2),
                ProdutoNome = produto.NomeProduto
            });
        }
        else
        {
            var indice = Itens.IndexOf(_itemSelecionado);
            if (indice < 0)
            {
                await DisplayAlertAsync("Erro", "O item selecionado não está mais disponível.", "OK");
                return;
            }

            Itens[indice] = new ItemCompra
            {
                CodigoItem = _itemSelecionado.CodigoItem,
                CodigoProduto = produto.CodigoProduto,
                CodigoCompra = _compraSelecionada?.CodigoCompra ?? 0,
                Quantidade = quantidade,
                ValorParcial = Math.Round(produto.Preco * quantidade, 2),
                ProdutoNome = produto.NomeProduto
            };
        }

        LimparEdicaoItem();
        AtualizarTotal();
    }

    private async void OnRemoverItemClicked(object? sender, EventArgs e)
    {
        if (_itemSelecionado is null)
        {
            await DisplayAlertAsync("Atenção", "Selecione um item para remover.", "OK");
            return;
        }

        Itens.Remove(_itemSelecionado);
        LimparEdicaoItem();
        AtualizarTotal();
    }

    private async void OnSalvarCompraClicked(object? sender, EventArgs e)
    {
        if (ClientePicker.SelectedItem is not Cliente cliente)
        {
            await DisplayAlertAsync("Validação", "Selecione o cliente da compra.", "OK");
            return;
        }

        try
        {
            var compra = _compraSelecionada ?? new Compra();
            compra.CodigoCliente = cliente.CodigoCliente;
            compra.ClienteNome = cliente.Nome;
            compra.DataCompra = DataCompraPicker.Date ?? DateTime.Today;

            var nova = compra.CodigoCompra == 0;
            _database.SalvarCompra(compra, Itens);

            await DisplayAlertAsync(
                "Sucesso",
                nova ? "Compra cadastrada e estoque atualizado." : "Compra atualizada e estoque recalculado.",
                "OK");
            LimparCompra();
            CarregarDados();
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

    private void OnNovaCompraClicked(object? sender, EventArgs e)
    {
        LimparCompra();
    }

    private async void OnExcluirCompraClicked(object? sender, EventArgs e)
    {
        if (_compraSelecionada is null)
        {
            await DisplayAlertAsync("Atenção", "Selecione uma compra para excluir.", "OK");
            return;
        }

        var confirmar = await DisplayAlertAsync(
            "Excluir compra",
            $"Deseja excluir a compra #{_compraSelecionada.CodigoCompra}? O estoque será devolvido.",
            "Excluir",
            "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            _database.ExcluirCompra(_compraSelecionada.CodigoCompra);
            await DisplayAlertAsync("Sucesso", "Compra excluída e estoque devolvido.", "OK");
            LimparCompra();
            CarregarDados();
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

    private void OnCompraSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _compraSelecionada = e.CurrentSelection.FirstOrDefault() as Compra;
        if (_compraSelecionada is null)
        {
            return;
        }

        ClientePicker.SelectedItem = Clientes.FirstOrDefault(
            cliente => cliente.CodigoCliente == _compraSelecionada.CodigoCliente);
        DataCompraPicker.Date = _compraSelecionada.DataCompra;

        Itens.Clear();
        foreach (var item in _database.ObterItensCompra(_compraSelecionada.CodigoCompra))
        {
            Itens.Add(item);
        }

        LimparEdicaoItem();
        TituloCompra.Text = $"Editando compra #{_compraSelecionada.CodigoCompra}";
        SalvarCompraButton.Text = "Atualizar compra";
        AtualizarTotal();
    }

    private void OnItemSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _itemSelecionado = e.CurrentSelection.FirstOrDefault() as ItemCompra;
        if (_itemSelecionado is null)
        {
            return;
        }

        ProdutoPicker.SelectedItem = Produtos.FirstOrDefault(
            produto => produto.CodigoProduto == _itemSelecionado.CodigoProduto);
        QuantidadeEntry.Text = _itemSelecionado.Quantidade.ToString(CultureInfo.CurrentCulture);
        SalvarItemButton.Text = "Atualizar item";
    }

    private void CarregarDados()
    {
        Clientes.Clear();
        foreach (var cliente in _database.ObterClientes())
        {
            Clientes.Add(cliente);
        }

        Produtos.Clear();
        foreach (var produto in _database.ObterProdutos())
        {
            Produtos.Add(produto);
        }

        Compras.Clear();
        foreach (var compra in _database.ObterCompras())
        {
            Compras.Add(compra);
        }
    }

    private void LimparCompra()
    {
        _compraSelecionada = null;
        ComprasCollection.SelectedItem = null;
        ClientePicker.SelectedItem = null;
        DataCompraPicker.Date = DateTime.Today;
        Itens.Clear();
        TituloCompra.Text = "Nova compra";
        SalvarCompraButton.Text = "Salvar compra";
        LimparEdicaoItem();
        AtualizarTotal();
    }

    private void LimparEdicaoItem()
    {
        _itemSelecionado = null;
        ItensCollection.SelectedItem = null;
        ProdutoPicker.SelectedItem = null;
        QuantidadeEntry.Text = string.Empty;
        SalvarItemButton.Text = "Adicionar item";
    }

    private void AtualizarTotal()
    {
        var total = Itens.Sum(item => item.ValorParcial);
        TotalLabel.Text = $"Total da compra: {total:C2}";
    }
}
