using DocuCentral.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<RolPermiso> RolPermisos => Set<RolPermiso>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<UsuarioAreaAcceso> UsuarioAreas => Set<UsuarioAreaAcceso>();
    public DbSet<TipoDocumento> TiposDocumento => Set<TipoDocumento>();
    public DbSet<CampoTipoDocumento> CamposTipoDocumento => Set<CampoTipoDocumento>();
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<DocumentoValor> DocumentoValores => Set<DocumentoValor>();
    public DbSet<DocumentoVersion> DocumentoVersiones => Set<DocumentoVersion>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();
    public DbSet<ParametroSistema> Parametros => Set<ParametroSistema>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Nombres de tabla explícitos y en minúsculas para que funcionen igual en los 3 motores
        b.Entity<Area>(e =>
        {
            e.ToTable("areas");
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
        });

        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.Property(x => x.NombreUsuario).HasMaxLength(100).IsRequired();
            e.Property(x => x.NombreCompleto).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.HasIndex(x => x.NombreUsuario).IsUnique();
            // Restrict: las áreas no se borran (se desactivan) y evita rutas de cascada múltiples en SQL Server
            e.HasOne(x => x.Area).WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Rol>(e =>
        {
            e.ToTable("roles");
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        b.Entity<RolPermiso>(e =>
        {
            e.ToTable("rol_permisos");
            e.HasKey(x => new { x.RolId, x.Permiso });
            e.Property(x => x.Permiso).HasMaxLength(60);
            e.HasOne(x => x.Rol).WithMany(r => r.Permisos).HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UsuarioRol>(e =>
        {
            e.ToTable("usuario_roles");
            e.HasKey(x => new { x.UsuarioId, x.RolId });
            e.HasOne(x => x.Usuario).WithMany(u => u.Roles).HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UsuarioAreaAcceso>(e =>
        {
            e.ToTable("usuario_areas");
            e.HasKey(x => new { x.UsuarioId, x.AreaId });
            e.HasOne(x => x.Usuario).WithMany(u => u.AreasAdicionales).HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Area).WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TipoDocumento>(e =>
        {
            e.ToTable("tipos_documento");
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        b.Entity<CampoTipoDocumento>(e =>
        {
            e.ToTable("campos_tipo_documento");
            e.Property(x => x.Clave).HasMaxLength(60).IsRequired();
            e.Property(x => x.Etiqueta).HasMaxLength(150).IsRequired();
            e.Property(x => x.Opciones).HasMaxLength(2000);
            e.HasIndex(x => new { x.TipoDocumentoId, x.Clave }).IsUnique();
            e.HasOne(x => x.TipoDocumento).WithMany(t => t.Campos).HasForeignKey(x => x.TipoDocumentoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Documento>(e =>
        {
            e.ToTable("documentos");
            e.Property(x => x.Titulo).HasMaxLength(300).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.Identificador).HasMaxLength(60);
            e.Property(x => x.NombreTitular).HasMaxLength(250);
            e.Property(x => x.Etiquetas).HasMaxLength(500);
            e.HasIndex(x => x.Identificador);
            e.HasIndex(x => x.NombreTitular);
            e.HasIndex(x => new { x.Estado, x.AreaId });
            e.HasIndex(x => x.TipoDocumentoId);
            e.HasIndex(x => x.CreadoEn);
            e.HasOne(x => x.TipoDocumento).WithMany().HasForeignKey(x => x.TipoDocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Area).WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreadoPor).WithMany().HasForeignKey(x => x.CreadoPorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentoValor>(e =>
        {
            e.ToTable("documento_valores");
            e.Property(x => x.Valor).HasMaxLength(1000);
            e.HasIndex(x => new { x.CampoId, x.Valor });
            e.HasOne(x => x.Documento).WithMany(d => d.Valores).HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: SQL Server no admite dos rutas de borrado en cascada hacia la misma tabla
            e.HasOne(x => x.Campo).WithMany().HasForeignKey(x => x.CampoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentoVersion>(e =>
        {
            e.ToTable("documento_versiones");
            e.Property(x => x.NombreOriginal).HasMaxLength(300).IsRequired();
            e.Property(x => x.RutaRelativa).HasMaxLength(600).IsRequired();
            e.Property(x => x.Extension).HasMaxLength(20);
            e.Property(x => x.TipoMime).HasMaxLength(150);
            e.Property(x => x.HashSha256).HasMaxLength(64);
            e.Property(x => x.Comentario).HasMaxLength(500);
            e.HasIndex(x => new { x.DocumentoId, x.NumeroVersion }).IsUnique();
            e.HasIndex(x => x.HashSha256);
            e.HasOne(x => x.Documento).WithMany(d => d.Versiones).HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreadoPor).WithMany().HasForeignKey(x => x.CreadoPorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RegistroAuditoria>(e =>
        {
            e.ToTable("auditoria");
            e.Property(x => x.NombreUsuario).HasMaxLength(100);
            e.Property(x => x.Accion).HasMaxLength(40).IsRequired();
            e.Property(x => x.Entidad).HasMaxLength(60);
            e.Property(x => x.EntidadId).HasMaxLength(60);
            e.Property(x => x.Detalle).HasMaxLength(2000);
            e.Property(x => x.DireccionIp).HasMaxLength(64);
            e.HasIndex(x => x.Fecha);
            e.HasIndex(x => new { x.Entidad, x.EntidadId });
            e.HasIndex(x => x.UsuarioId);
        });

        b.Entity<ParametroSistema>(e =>
        {
            e.ToTable("parametros");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Clave).HasMaxLength(100);
            e.Property(x => x.Valor).HasMaxLength(1000);
        });
    }
}
