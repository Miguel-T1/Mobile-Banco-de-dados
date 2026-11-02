namespace TdeMauiBanco.Model;

public sealed class Compra
{
    public long CodigoCompra { get; set; }
    public DateTime DataCompra { get; set; } = DateTime.Today;
    public decimal ValorTotal { get; set; }
    public long CodigoCliente { get; set; }
    public string ClienteNome { get; set; } = string.Empty;

    public string Descricao =>
        $"Compra #{CodigoCompra} - {DataCompra:dd/MM/yyyy} - {ClienteNome} - {ValorTotal:C2}";
}
