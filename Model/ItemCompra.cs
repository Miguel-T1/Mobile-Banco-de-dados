namespace TdeMauiBanco.Model;

public sealed class ItemCompra
{
    public long CodigoItem { get; set; }
    public long CodigoProduto { get; set; }
    public long CodigoCompra { get; set; }
    public int Quantidade { get; set; }
    public decimal ValorParcial { get; set; }
    public string ProdutoNome { get; set; } = string.Empty;

    public decimal PrecoUnitario =>
        Quantidade == 0 ? 0 : Math.Round(ValorParcial / Quantidade, 2);

    public string Descricao =>
        $"{ProdutoNome} - {Quantidade} x {PrecoUnitario:C2} = {ValorParcial:C2}";
}
