namespace TdeMauiBanco.Model;

public sealed class Produto
{
    public long CodigoProduto { get; set; }
    // Mantém o mesmo nome do atributo especificado no enunciado e no banco.
    public string NomeProduto { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public int QtdEstoque { get; set; }

    public string Descricao => $"{NomeProduto} - {Preco:C2} ({QtdEstoque} em estoque)";
}
