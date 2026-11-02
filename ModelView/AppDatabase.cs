namespace TdeMauiBanco.ModelView;

/// <summary>
/// Cria uma única instância do serviço de dados somente quando uma página de
/// cadastro é aberta. Isso mantém a abertura do menu principal independente
/// da existência prévia do arquivo SQLite.
/// </summary>
public static class AppDatabase
{
    private static readonly Lazy<DatabaseService> Database =
        new(() => new DatabaseService());

    public static DatabaseService Instance => Database.Value;
}
