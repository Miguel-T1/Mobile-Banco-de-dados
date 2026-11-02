namespace TdeMauiBanco.Model;

public sealed class Cliente
{
    public long CodigoCliente { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Rg { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;

    public override string ToString() => Nome;
}
