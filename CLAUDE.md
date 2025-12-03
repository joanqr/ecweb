# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

EdoCuentaWeb is an ASP.NET MVC 5 web application for managing account statements and customer service for two financial companies: Conauto and Fina. The application targets .NET Framework 4.7.1 and uses a multi-layered architecture.

## Solution Structure

The solution (`BBCuentas.sln`) consists of 5 projects:

1. **BBCuentas** - Main ASP.NET MVC web application (presentation layer)
2. **BusinessLayer** - Business logic layer
3. **DataLayer** - Data access layer with Repository and Unit of Work patterns
4. **ModelLayer** - Domain models and DTOs
5. **r3Take** - Utility library for data access (DAL), encryption, and AMF utilities

## Architecture

### Layered Architecture Pattern

The application follows a strict layered architecture:

```
Controllers (BBCuentas)
    ↓
Business Layer (Usuario_Business, Contrato_Business, etc.)
    ↓
Data Layer (Repositories + Unit of Work)
    ↓
Database (SQL Server)
```

- **Controllers** handle HTTP requests and return views or JSON
- **Business Layer** contains business logic and validation
- **Data Layer** uses Repository pattern for data access and Unit of Work for transaction management
- **Models** are shared across all layers

### Unit of Work Pattern

The DataLayer implements the Unit of Work pattern with factory creation:

- `UnitOfWorkFactory.Create()` - Creates UoW for main `ecweb` database
- `UnitOfWorkFactory.CreateCanauto()` - Creates UoW for `conautoss` database
- Always use `using` statements to ensure proper disposal

Example:
```csharp
using (var uow = UnitOfWorkFactory.Create())
{
    var repository = new Usuario_Repository(uow);
    var response = repository.ValidarUsuario(usuario);
    return response;
}
```

### Dual-Company System

The application serves two companies with different branding:

- **Fina** (iCompania = 1) - Financiera company
- **Conauto** (iCompania = 2) - Automotive financing company

User routing is determined by `DeterminarTipoEmpresaUsuario()` in LoginController.cs:125, which checks contract company assignment and stores `TipoEmpresa` in cookies for navigation.

## Database Configuration

Connection strings are in `Web.config`:

- **ConnectionString** - Main database `ecweb` on 172.20.54.132
- **ConnectionStringConautoss** - Secondary database `conautoss` on same server
- **DS_ECWEB** - Alternative connection string used by r3Take.DAL

The r3Take.DAL class is used for direct database queries and stored procedure execution throughout the application.

## Key Components

### Authentication & Security

- Forms Authentication with 20-minute timeout
- Login redirects to HTTPS (except localhost)
- Passwords are MD5 hashed via `EncriptaPassword.GetMD5()`
- Token-based password recovery system
- Default route: `Login/Index`

### External Web Services

Three SOAP web services are consumed via Connected Services:

1. **WSValidaEmpresa** - Validates customer data against enterprise systems
2. **WSGetEstadoCuenta** - Retrieves account statements (PDF generation)
3. **AppRemota** - Remote application services for policy operations (Fina/Conauto)

All endpoints configured in `Web.config` under `<system.serviceModel>`

### Controllers Organization

Main controllers by functionality:

- **LoginController** - Authentication, user registration, password recovery
- **FinaController** / **AccountStatementController** - Account statements for respective companies
- **SecurityController** - Security settings
- **AdmonController** - Administrative functions
- **AtencionClientesController** - Customer service
- **MaintenanceController** - Maintenance mode management

### Helper Classes

- **EncriptaPassword** - MD5 password hashing
- **wsEsweb** - Wrapper for WSValidaEmpresa web service
- **AppRemota** - Wrapper for remote application services
- **EnviaEmail** - Email sending functionality (Office365 SMTP)

### Front-End Stack

- Bootstrap 4
- jQuery 3.0
- DataTables for table management
- SASS/SCSS compilation via BundleTransformer
- Custom CSS in `Styles/` directory

## Build & Development

### Building the Project

Since MSBuild may not be in PATH, use Visual Studio or specify the full path:

```bash
# Using Visual Studio 2017+
msbuild BBCuentas.sln /p:Configuration=Release

# Or open in Visual Studio and build via IDE
```

### Running Locally

The application runs on IIS Express at `http://localhost:2850/`

Configuration is in `.csproj` file PropertyGroup (lines 410-421 in BBCuentas.csproj)

### Configuration Files

- **Web.config** - Main configuration with connection strings, app settings, and service endpoints
- **Web.Debug.config** / **Web.Release.config** - Transform files for different environments
- **packages.config** - NuGet package references

### Important AppSettings

- `CarpetaLocal` - Local file storage path (C:/Archivos)
- `IPCarpeta` - File server URL
- `UrlNuevo` - Password reset URL base
- `ApiEDCHistoricosUrl` - API endpoint for historical account statements
- `PermitirEnvioSMS` - SMS sending toggle

## Common Development Workflows

### Adding a New Repository

1. Create interface and implementation in `DataLayer/Repositories/`
2. Inject UnitOfWork in constructor
3. Use UoW to create database commands
4. Create corresponding Business class in `BusinessLayer/`
5. Reference in controllers

### Working with Database

The application uses two patterns for data access:

1. **Repository Pattern** - For CRUD operations on entities
2. **Direct DAL Usage** - For complex queries and stored procedures

Example using r3Take.DAL:
```csharp
DAL dal = new DAL();
Hashtable hashTableParameters = new Hashtable();
hashTableParameters.Add("paramName", value);
DataTable dt = dal.QueryDT("DS_ECWEB", "SELECT...", "H:S:paramName", hashTableParameters, HttpContext.Current);
```

### Stored Procedures

Execute via DAL.ExecuteSP():
```csharp
List<DBParam> lParam = new List<DBParam>();
lParam.Add(new DBParam("Usuario", email, "s"));
dal.ExecuteSP("DS_ECWEB", "Olv_Contrasena", lParam, HttpContext.Current);
```

## Deployment

Publish profiles available in `Properties/PublishProfiles/`:
- **IISProfile.pubxml** - IIS deployment profile
- **CustomProfile.pubxml** - Custom deployment configuration

The application includes auto-HTTPS redirect rules in Web.config (lines 124-134) that skip localhost.

## Maintenance Mode

The system supports a maintenance mode controlled by database configuration:
- Table: `[dbo].[Configuraciones]`
- Column: `PaginaEnMantenimiento` (boolean)
- When enabled, only administrator email can access the site
- Others are redirected to `/Manteni/Index.html`

## Notes

- TypeScript is configured but appears minimally used
- SASS/SCSS compilation happens at runtime via BundleTransformer
- Solution uses .NET Framework 4.7.1 (not .NET Core/5+)
- Spanish is the primary language for UI and comments
