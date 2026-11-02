using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;
using TdeMauiBanco.Infrastructure;
using TdeMauiBanco.Model;

namespace TdeMauiBanco.ModelView;

/// <summary>
/// Serviço da camada ModelView, organizado a partir do padrão do SQLApp da aula.
/// Centraliza a criação do esquema, o CRUD e as regras de negócio; as telas da
/// camada View apenas exibem dados e encaminham a intenção do usuário.
/// </summary>
public sealed class DatabaseService
{
    private static readonly Regex CaracteresNaoNumericos = new(@"\D", RegexOptions.Compiled);
    private static readonly Regex DocumentoOuTelefoneValido =
        new(@"^[0-9 .()/-]+$", RegexOptions.Compiled);

    private readonly string _databasePath;

    public DatabaseService()
    {
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "tde-banco-relacional.db3");
        CriarEsquemaSeNecessario();
    }

    public string DatabasePath => _databasePath;

    public List<Cliente> ObterClientes()
    {
        using var db = Abrir();
        return db.Query(
            """
            SELECT CodigoCliente, Nome, Rg, Telefone
            FROM Cliente
            ORDER BY Nome COLLATE NOCASE;
            """,
            statement => new Cliente
            {
                CodigoCliente = statement.GetInt64(0),
                Nome = statement.GetText(1),
                Rg = statement.GetText(2),
                Telefone = statement.GetText(3)
            });
    }

    public Cliente SalvarCliente(Cliente cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        cliente.Nome = NormalizarTexto(cliente.Nome);
        cliente.Rg = NormalizarTexto(cliente.Rg);
        cliente.Telefone = NormalizarTexto(cliente.Telefone);
        ValidarCliente(cliente);

        using var db = Abrir();
        if (cliente.CodigoCliente == 0)
        {
            cliente.CodigoCliente = db.Insert(
                "INSERT INTO Cliente (Nome, Rg, Telefone) VALUES (?, ?, ?);",
                cliente.Nome,
                cliente.Rg,
                cliente.Telefone);
        }
        else
        {
            db.Execute(
                """
                UPDATE Cliente
                SET Nome = ?, Rg = ?, Telefone = ?
                WHERE CodigoCliente = ?;
                """,
                cliente.Nome,
                cliente.Rg,
                cliente.Telefone,
                cliente.CodigoCliente);

            if (db.Changes == 0)
            {
                throw new BusinessRuleException("Cliente não encontrado para atualização.");
            }
        }

        return cliente;
    }

    public void ExcluirCliente(long codigoCliente)
    {
        using var db = Abrir();

        if (db.ScalarLong(
                "SELECT COUNT(*) FROM Compra WHERE CodigoCliente = ?;",
                codigoCliente) > 0)
        {
            throw new BusinessRuleException(
                "Não é possível excluir este cliente porque ele possui compras registradas.");
        }

        db.Execute("DELETE FROM Cliente WHERE CodigoCliente = ?;", codigoCliente);
        if (db.Changes == 0)
        {
            throw new BusinessRuleException("Cliente não encontrado para exclusão.");
        }
    }

    public List<Produto> ObterProdutos()
    {
        using var db = Abrir();
        return db.Query(
            """
            SELECT CodigoProduto, NomeProduto, Tipo, Preco, QtdEstoque
            FROM Produto
            ORDER BY NomeProduto COLLATE NOCASE;
            """,
            MapearProduto);
    }

    public Produto SalvarProduto(Produto produto)
    {
        ArgumentNullException.ThrowIfNull(produto);

        produto.NomeProduto = NormalizarTexto(produto.NomeProduto);
        produto.Tipo = NormalizarTexto(produto.Tipo);
        produto.Preco = ArredondarMoeda(produto.Preco);
        ValidarProduto(produto);

        using var db = Abrir();
        if (produto.CodigoProduto == 0)
        {
            produto.CodigoProduto = db.Insert(
                """
                INSERT INTO Produto (NomeProduto, Tipo, Preco, QtdEstoque)
                VALUES (?, ?, ?, ?);
                """,
                produto.NomeProduto,
                produto.Tipo,
                produto.Preco,
                produto.QtdEstoque);
        }
        else
        {
            db.Execute(
                """
                UPDATE Produto
                SET NomeProduto = ?, Tipo = ?, Preco = ?, QtdEstoque = ?
                WHERE CodigoProduto = ?;
                """,
                produto.NomeProduto,
                produto.Tipo,
                produto.Preco,
                produto.QtdEstoque,
                produto.CodigoProduto);

            if (db.Changes == 0)
            {
                throw new BusinessRuleException("Produto não encontrado para atualização.");
            }
        }

        return produto;
    }

    public void ExcluirProduto(long codigoProduto)
    {
        using var db = Abrir();

        if (db.ScalarLong(
                "SELECT COUNT(*) FROM ItemCompra WHERE CodigoProduto = ?;",
                codigoProduto) > 0)
        {
            throw new BusinessRuleException(
                "Não é possível excluir este produto porque ele faz parte de uma compra.");
        }

        db.Execute("DELETE FROM Produto WHERE CodigoProduto = ?;", codigoProduto);
        if (db.Changes == 0)
        {
            throw new BusinessRuleException("Produto não encontrado para exclusão.");
        }
    }

    public List<Compra> ObterCompras()
    {
        using var db = Abrir();
        return db.Query(
            """
            SELECT c.CodigoCompra, c.DataCompra, c.ValorTotal, c.CodigoCliente, cl.Nome
            FROM Compra c
            INNER JOIN Cliente cl ON cl.CodigoCliente = c.CodigoCliente
            ORDER BY c.DataCompra DESC, c.CodigoCompra DESC;
            """,
            statement => new Compra
            {
                CodigoCompra = statement.GetInt64(0),
                DataCompra = LerData(statement.GetText(1)),
                ValorTotal = LerMoeda(statement.GetDouble(2)),
                CodigoCliente = statement.GetInt64(3),
                ClienteNome = statement.GetText(4)
            });
    }

    public List<ItemCompra> ObterItensCompra(long codigoCompra)
    {
        using var db = Abrir();
        return ObterItensCompra(db, codigoCompra, incluirNomeProduto: true);
    }

    /// <summary>
    /// Cria ou atualiza uma compra e substitui seus itens dentro de uma transação.
    /// Na atualização, o estoque dos itens antigos é devolvido antes de reservar
    /// o estoque da nova lista, permitindo alterar quantidade ou produto.
    /// </summary>
    public Compra SalvarCompra(Compra compra, IEnumerable<ItemCompra> itensRecebidos)
    {
        ArgumentNullException.ThrowIfNull(compra);
        ArgumentNullException.ThrowIfNull(itensRecebidos);

        var itens = itensRecebidos.Select(CopiarItem).ToList();
        ValidarCompra(compra, itens);

        using var db = Abrir();
        db.BeginTransaction();
        try
        {
            if (db.ScalarLong(
                    "SELECT COUNT(*) FROM Cliente WHERE CodigoCliente = ?;",
                    compra.CodigoCliente) == 0)
            {
                throw new BusinessRuleException("Selecione um cliente válido para a compra.");
            }

            var novaCompra = compra.CodigoCompra == 0;
            if (!novaCompra && db.ScalarLong(
                    "SELECT COUNT(*) FROM Compra WHERE CodigoCompra = ?;",
                    compra.CodigoCompra) == 0)
            {
                throw new BusinessRuleException("Compra não encontrada para atualização.");
            }

            var itensAntigos = novaCompra
                ? new List<ItemCompra>()
                : ObterItensCompra(db, compra.CodigoCompra, incluirNomeProduto: false);

            foreach (var item in itens)
            {
                var produto = BuscarProduto(db, item.CodigoProduto)
                    ?? throw new BusinessRuleException("Um dos produtos selecionados não existe mais.");

                var quantidadeAnterior = itensAntigos
                    .Where(antigo => antigo.CodigoProduto == item.CodigoProduto)
                    .Sum(antigo => antigo.Quantidade);

                if (item.Quantidade > produto.QtdEstoque + quantidadeAnterior)
                {
                    throw new BusinessRuleException(
                        $"Estoque insuficiente para o produto \"{produto.NomeProduto}\".");
                }

                item.ProdutoNome = produto.NomeProduto;
                item.ValorParcial = ArredondarMoeda(produto.Preco * item.Quantidade);
            }

            compra.DataCompra = compra.DataCompra.Date;
            compra.ValorTotal = ArredondarMoeda(itens.Sum(item => item.ValorParcial));

            if (novaCompra)
            {
                compra.CodigoCompra = db.Insert(
                    """
                    INSERT INTO Compra (DataCompra, ValorTotal, CodigoCliente)
                    VALUES (?, ?, ?);
                    """,
                    compra.DataCompra,
                    compra.ValorTotal,
                    compra.CodigoCliente);
            }
            else
            {
                db.Execute(
                    """
                    UPDATE Compra
                    SET DataCompra = ?, ValorTotal = ?, CodigoCliente = ?
                    WHERE CodigoCompra = ?;
                    """,
                    compra.DataCompra,
                    compra.ValorTotal,
                    compra.CodigoCliente,
                    compra.CodigoCompra);
            }

            foreach (var itemAntigo in itensAntigos)
            {
                db.Execute(
                    """
                    UPDATE Produto
                    SET QtdEstoque = QtdEstoque + ?
                    WHERE CodigoProduto = ?;
                    """,
                    itemAntigo.Quantidade,
                    itemAntigo.CodigoProduto);
            }

            if (!novaCompra)
            {
                db.Execute("DELETE FROM ItemCompra WHERE CodigoCompra = ?;", compra.CodigoCompra);
            }

            foreach (var item in itens)
            {
                db.Execute(
                    """
                    UPDATE Produto
                    SET QtdEstoque = QtdEstoque - ?
                    WHERE CodigoProduto = ?;
                    """,
                    item.Quantidade,
                    item.CodigoProduto);

                item.CodigoCompra = compra.CodigoCompra;
                item.CodigoItem = db.Insert(
                    """
                    INSERT INTO ItemCompra
                        (CodigoProduto, CodigoCompra, Quantidade, ValorParcial)
                    VALUES (?, ?, ?, ?);
                    """,
                    item.CodigoProduto,
                    item.CodigoCompra,
                    item.Quantidade,
                    item.ValorParcial);
            }

            db.Commit();
            return compra;
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public void ExcluirCompra(long codigoCompra)
    {
        using var db = Abrir();
        db.BeginTransaction();
        try
        {
            if (db.ScalarLong(
                    "SELECT COUNT(*) FROM Compra WHERE CodigoCompra = ?;",
                    codigoCompra) == 0)
            {
                throw new BusinessRuleException("Compra não encontrada para exclusão.");
            }

            var itens = ObterItensCompra(db, codigoCompra, incluirNomeProduto: false);
            foreach (var item in itens)
            {
                db.Execute(
                    """
                    UPDATE Produto
                    SET QtdEstoque = QtdEstoque + ?
                    WHERE CodigoProduto = ?;
                    """,
                    item.Quantidade,
                    item.CodigoProduto);
            }

            db.Execute("DELETE FROM ItemCompra WHERE CodigoCompra = ?;", codigoCompra);
            db.Execute("DELETE FROM Compra WHERE CodigoCompra = ?;", codigoCompra);
            db.Commit();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    private void CriarEsquemaSeNecessario()
    {
        using var db = Abrir();
        foreach (var comando in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS Cliente (
                CodigoCliente INTEGER PRIMARY KEY AUTOINCREMENT,
                Nome TEXT NOT NULL,
                Rg TEXT NOT NULL,
                Telefone TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Produto (
                CodigoProduto INTEGER PRIMARY KEY AUTOINCREMENT,
                NomeProduto TEXT NOT NULL,
                Tipo TEXT NOT NULL,
                Preco REAL NOT NULL CHECK (Preco > 0),
                QtdEstoque INTEGER NOT NULL CHECK (QtdEstoque >= 0)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Compra (
                CodigoCompra INTEGER PRIMARY KEY AUTOINCREMENT,
                DataCompra TEXT NOT NULL,
                ValorTotal REAL NOT NULL CHECK (ValorTotal >= 0),
                CodigoCliente INTEGER NOT NULL,
                FOREIGN KEY (CodigoCliente)
                    REFERENCES Cliente (CodigoCliente)
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ItemCompra (
                CodigoItem INTEGER PRIMARY KEY AUTOINCREMENT,
                CodigoProduto INTEGER NOT NULL,
                CodigoCompra INTEGER NOT NULL,
                Quantidade INTEGER NOT NULL CHECK (Quantidade > 0),
                ValorParcial REAL NOT NULL CHECK (ValorParcial > 0),
                FOREIGN KEY (CodigoProduto)
                    REFERENCES Produto (CodigoProduto)
                    ON DELETE RESTRICT,
                FOREIGN KEY (CodigoCompra)
                    REFERENCES Compra (CodigoCompra)
                    ON DELETE RESTRICT,
                UNIQUE (CodigoCompra, CodigoProduto)
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_Compra_Cliente ON Compra (CodigoCliente);",
            "CREATE INDEX IF NOT EXISTS IX_ItemCompra_Produto ON ItemCompra (CodigoProduto);",
            "CREATE INDEX IF NOT EXISTS IX_ItemCompra_Compra ON ItemCompra (CodigoCompra);"
        })
        {
            db.Execute(comando);
        }
    }

    private NativeSqliteConnection Abrir() => new(_databasePath);

    private static Produto? BuscarProduto(NativeSqliteConnection db, long codigoProduto)
    {
        return db.Query(
                """
                SELECT CodigoProduto, NomeProduto, Tipo, Preco, QtdEstoque
                FROM Produto
                WHERE CodigoProduto = ?;
                """,
                MapearProduto,
                codigoProduto)
            .FirstOrDefault();
    }

    private static List<ItemCompra> ObterItensCompra(
        NativeSqliteConnection db,
        long codigoCompra,
        bool incluirNomeProduto)
    {
        var selectProduto = incluirNomeProduto
            ? ", p.NomeProduto"
            : ", ''";
        var joinProduto = incluirNomeProduto
            ? "INNER JOIN Produto p ON p.CodigoProduto = i.CodigoProduto"
            : string.Empty;

        return db.Query(
            $"""
            SELECT i.CodigoItem, i.CodigoProduto, i.CodigoCompra, i.Quantidade, i.ValorParcial{selectProduto}
            FROM ItemCompra i
            {joinProduto}
            WHERE i.CodigoCompra = ?
            ORDER BY i.CodigoItem;
            """,
            statement => new ItemCompra
            {
                CodigoItem = statement.GetInt64(0),
                CodigoProduto = statement.GetInt64(1),
                CodigoCompra = statement.GetInt64(2),
                Quantidade = statement.GetInt32(3),
                ValorParcial = LerMoeda(statement.GetDouble(4)),
                ProdutoNome = statement.GetText(5)
            },
            codigoCompra);
    }

    private static Produto MapearProduto(NativeSqliteStatement statement)
    {
        return new Produto
        {
            CodigoProduto = statement.GetInt64(0),
            NomeProduto = statement.GetText(1),
            Tipo = statement.GetText(2),
            Preco = LerMoeda(statement.GetDouble(3)),
            QtdEstoque = statement.GetInt32(4)
        };
    }

    private static ItemCompra CopiarItem(ItemCompra item)
    {
        return new ItemCompra
        {
            CodigoItem = item.CodigoItem,
            CodigoProduto = item.CodigoProduto,
            CodigoCompra = item.CodigoCompra,
            Quantidade = item.Quantidade,
            ValorParcial = item.ValorParcial,
            ProdutoNome = item.ProdutoNome
        };
    }

    private static void ValidarCliente(Cliente cliente)
    {
        if (string.IsNullOrWhiteSpace(cliente.Nome))
        {
            throw new BusinessRuleException("Informe o nome do cliente.");
        }

        if (cliente.Nome.Length > 50)
        {
            throw new BusinessRuleException("O nome do cliente deve ter no máximo 50 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(cliente.Rg))
        {
            throw new BusinessRuleException("Informe o RG do cliente.");
        }

        if (!DocumentoOuTelefoneValido.IsMatch(cliente.Rg))
        {
            throw new BusinessRuleException("O RG só pode conter números e caracteres de formatação.");
        }

        var rgNumerico = CaracteresNaoNumericos.Replace(cliente.Rg, string.Empty);
        if (rgNumerico.Length is < 5 or > 14)
        {
            throw new BusinessRuleException("Informe um RG com entre 5 e 14 dígitos.");
        }

        if (string.IsNullOrWhiteSpace(cliente.Telefone))
        {
            throw new BusinessRuleException("Informe o telefone do cliente.");
        }

        if (!DocumentoOuTelefoneValido.IsMatch(cliente.Telefone))
        {
            throw new BusinessRuleException("O telefone só pode conter números e caracteres de formatação.");
        }

        var telefoneNumerico = CaracteresNaoNumericos.Replace(cliente.Telefone, string.Empty);
        if (telefoneNumerico.Length is < 8 or > 15)
        {
            throw new BusinessRuleException("Informe um telefone com entre 8 e 15 dígitos.");
        }
    }

    private static void ValidarProduto(Produto produto)
    {
        if (string.IsNullOrWhiteSpace(produto.NomeProduto))
        {
            throw new BusinessRuleException("Informe o nome do produto.");
        }

        if (produto.NomeProduto.Length > 50)
        {
            throw new BusinessRuleException("O nome do produto deve ter no máximo 50 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(produto.Tipo))
        {
            throw new BusinessRuleException("Informe o tipo do produto.");
        }

        if (produto.Tipo.Length > 50)
        {
            throw new BusinessRuleException("O tipo do produto deve ter no máximo 50 caracteres.");
        }

        if (produto.Preco <= 0)
        {
            throw new BusinessRuleException("O preço deve ser maior que zero.");
        }

        if (produto.QtdEstoque < 0)
        {
            throw new BusinessRuleException("A quantidade em estoque não pode ser negativa.");
        }
    }

    private static void ValidarCompra(Compra compra, IReadOnlyCollection<ItemCompra> itens)
    {
        if (compra.CodigoCliente <= 0)
        {
            throw new BusinessRuleException("Selecione um cliente para a compra.");
        }

        if (compra.DataCompra == default)
        {
            throw new BusinessRuleException("Informe uma data de compra válida.");
        }

        if (itens.Count == 0)
        {
            throw new BusinessRuleException("Inclua pelo menos um item na compra.");
        }

        if (itens.Any(item => item.CodigoProduto <= 0))
        {
            throw new BusinessRuleException("Selecione um produto válido para cada item.");
        }

        if (itens.Any(item => item.Quantidade <= 0))
        {
            throw new BusinessRuleException("A quantidade de cada item deve ser maior que zero.");
        }

        if (itens.GroupBy(item => item.CodigoProduto).Any(grupo => grupo.Count() > 1))
        {
            throw new BusinessRuleException(
                "Um produto só pode aparecer uma vez na mesma compra. Edite a quantidade da linha existente.");
        }
    }

    private static string NormalizarTexto(string? valor) => valor?.Trim() ?? string.Empty;

    private static decimal ArredondarMoeda(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static decimal LerMoeda(double valor) =>
        ArredondarMoeda(Convert.ToDecimal(valor));

    private static DateTime LerData(string valor)
    {
        if (DateTime.TryParseExact(
                valor,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var data))
        {
            return data;
        }

        throw new InvalidOperationException(
            $"A compra contém uma data inválida no banco SQLite: \"{valor}\".");
    }
}
