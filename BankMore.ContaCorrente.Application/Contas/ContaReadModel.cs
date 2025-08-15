namespace BankMore.ContaCorrente.Application.Contas;

// Classe com construtor padrão e setters para o Dapper preencher por propriedades.
public sealed class ContaReadModel
{
    public string IdConta { get; set; } = default!;
    public int Numero { get; set; }
    public string Cpf { get; set; } = default!;
    public string Nome { get; set; } = default!;
    public bool Ativo { get; set; }
    public string SenhaHash { get; set; } = default!;
    public string Salt { get; set; } = default!;
}
