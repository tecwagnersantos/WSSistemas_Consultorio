using Consultorio.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Consultorio.Infrastructure.Security;

/// <summary>Usa o PasswordHasher do ASP.NET Core Identity (PBKDF2, formato V3). Serve para usuários das clínicas e administradores do sistema.</summary>
public class SenhaHasher : ISenhaHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object Dono = new();

    public string Gerar(string senha) => _hasher.HashPassword(Dono, senha);

    public bool Verificar(string hash, string senha) =>
        !string.IsNullOrEmpty(hash) && _hasher.VerifyHashedPassword(Dono, hash, senha) != PasswordVerificationResult.Failed;
}
