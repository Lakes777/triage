using Microsoft.EntityFrameworkCore;

namespace Triage.Servidor.Banco;

/// <summary>O banco do servidor. Nomes em snake_case (UseSnakeCaseNamingConvention), como no Beacon.</summary>
public sealed class TriageContexto(DbContextOptions<TriageContexto> opcoes) : DbContext(opcoes)
{
    public DbSet<Maquina> Maquinas => Set<Maquina>();
    public DbSet<CodigoDeInscricao> Codigos => Set<CodigoDeInscricao>();
    public DbSet<RevisaoGravada> Revisoes => Set<RevisaoGravada>();
    public DbSet<ResultadoGravado> Resultados => Set<ResultadoGravado>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Maquina>(m =>
        {
            m.ToTable("maquinas");
            m.Property(x => x.Id).ValueGeneratedNever();
            m.Property(x => x.Apelido).HasMaxLength(Regras.TamanhoMaximoDoApelido);
            m.Property(x => x.InscritaEm).HasDefaultValueSql("now()");
            m.HasIndex(x => x.ChaveHash).IsUnique();
        });

        modelo.Entity<CodigoDeInscricao>(c =>
        {
            c.ToTable("codigos_inscricao");
            c.Property(x => x.Id).ValueGeneratedNever();
            c.HasIndex(x => x.CodigoHash).IsUnique();
            c.HasOne<Maquina>().WithMany().HasForeignKey(x => x.MaquinaId);
        });

        modelo.Entity<RevisaoGravada>(r =>
        {
            r.ToTable("revisoes");
            r.HasKey(x => new { x.MaquinaId, x.Id });
            r.Property(x => x.VersaoDoAgente).HasMaxLength(64);
            r.Property(x => x.Foto).HasColumnType("jsonb");
            r.HasOne<Maquina>().WithMany().HasForeignKey(x => x.MaquinaId);
            // O histórico (fase 8) anda por máquina e pela hora em que o agente revisou
            r.HasIndex(x => new { x.MaquinaId, x.FeitaEm });
        });

        modelo.Entity<ResultadoGravado>(r =>
        {
            r.ToTable("resultados");
            r.HasKey(x => new { x.MaquinaId, x.RevisaoId, x.Item });
            r.Property(x => x.Item).HasMaxLength(64);
            r.Property(x => x.Gravidade).HasMaxLength(16);
            r.Property(x => x.Motivo).HasMaxLength(16);
            r.HasOne<RevisaoGravada>().WithMany().HasForeignKey(x => new { x.MaquinaId, x.RevisaoId });
        });
    }
}
