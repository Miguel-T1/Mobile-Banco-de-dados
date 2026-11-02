using System.Collections.ObjectModel;
using System.Globalization;
using TdeMauiBanco.Model;
using TdeMauiBanco.ModelView;

namespace TdeMauiBanco.View;

public partial class ProdutosPage : ContentPage
{
    private readonly DatabaseService _database;
    private Produto? _produtoSelecionado;

    public ProdutosPage()
        : this(AppDatabase.Instance)
    {
    }

    public ProdutosPage(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
        ProdutosCollection.ItemsSource = Produtos;
    }

    public ObservableCollection<Produto> Produtos { get; } = new();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CarregarProdutos();
    }

    private async void OnSalvarClicked(object? sender, EventArgs e)
    {
        try
        {
            var produto = _produtoSelecionado ?? new Produto();
            produto.NomeProduto = NomeEntry.Text ?? string.Empty;
            produto.Tipo = TipoEntry.Text ?? string.Empty;
            produto.Preco = LerPreco();
            produto.QtdEstoque = LerEstoque();

            var novo = produto.CodigoProduto == 0;
            _database.SalvarProduto(produto);

            await DisplayAlertAsync(
                "Sucesso",
                novo ? "Produto cadastrado." : "Produto atualizado.",
                "OK");
            LimparFormulario();
            CarregarProdutos();
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
        if (_produtoSelecionado is null)
        {
            await DisplayAlertAsync("Atenção", "Selecione um produto para excluir.", "OK");
            return;
        }

        var confirmar = await DisplayAlertAsync(
            "Excluir produto",
            $"Deseja excluir {_produtoSelecionado.NomeProduto}?",
            "Excluir",
            "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            _database.ExcluirProduto(_produtoSelecionado.CodigoProduto);
            await DisplayAlertAsync("Sucesso", "Produto excluído.", "OK");
            LimparFormulario();
            CarregarProdutos();
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

    private void OnProdutoSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _produtoSelecionado = e.CurrentSelection.FirstOrDefault() as Produto;
        if (_produtoSelecionado is null)
        {
            return;
        }

        NomeEntry.Text = _produtoSelecionado.NomeProduto;
        TipoEntry.Text = _produtoSelecionado.Tipo;
        PrecoEntry.Text = _produtoSelecionado.Preco.ToString("0.00", CultureInfo.CurrentCulture);
        EstoqueEntry.Text = _produtoSelecionado.QtdEstoque.ToString(CultureInfo.CurrentCulture);
        TituloFormulario.Text = $"Editando produto #{_produtoSelecionado.CodigoProduto}";
        SalvarButton.Text = "Atualizar produto";
    }

    private void CarregarProdutos()
    {
        Produtos.Clear();
        foreach (var produto in _database.ObterProdutos())
        {
            Produtos.Add(produto);
        }
    }

    private void LimparFormulario()
    {
        _produtoSelecionado = null;
        ProdutosCollection.SelectedItem = null;
        NomeEntry.Text = string.Empty;
        TipoEntry.Text = string.Empty;
        PrecoEntry.Text = string.Empty;
        EstoqueEntry.Text = string.Empty;
        TituloFormulario.Text = "Novo produto";
        SalvarButton.Text = "Cadastrar produto";
    }

    private decimal LerPreco()
    {
        var texto = PrecoEntry.Text?.Trim();
        if (decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var preco)
            || decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out preco))
        {
            return preco;
        }

        throw new BusinessRuleException("Informe um preço válido.");
    }

    private int LerEstoque()
    {
        if (int.TryParse(
                EstoqueEntry.Text?.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var estoque))
        {
            return estoque;
        }

        throw new BusinessRuleException("Informe uma quantidade inteira de estoque.");
    }
}
