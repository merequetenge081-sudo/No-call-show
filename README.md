# Call Shield (Windows 10/11)

Aplicación de escritorio para bloquear llamadas WebRTC de navegadores por proceso, con experiencia ON/OFF reversible.

## Arquitectura

- `src/CallShield.App` (WPF, .NET 8 Windows): UI, toggle, selector de modo, apps objetivo, horario y diagnóstico.
- `src/CallShield.Core` (lógica):
  - `PowerShellFirewallManager`: crea/elimina reglas `CallShield_*` en Windows Firewall.
  - `ShieldController`: orquesta enable/disable y logueo.
  - `JsonlLogService`: log local en `%LocalAppData%\CallShield\events.jsonl`.

## Modos

### Ligero (MVP recomendado)
- Bloqueo `Outbound UDP` por proceso objetivo (`chrome.exe`, `msedge.exe`, opcional `firefox.exe`).
- Mantiene TCP 443 para navegación/señalización en la mayoría de casos.

### Estricto
- Todo lo del modo Ligero.
- Bloqueo adicional TCP a puertos comunes TURN/STUN: `3478,3479,5349,19302-19309` por proceso objetivo.
- Puede impactar algunas funciones en tiempo real no-WebRTC.

### Total
- Bloqueo completo por proceso (`Inbound/Outbound Any`).
- Modo pánico.

## Lista exacta de reglas creadas

Prefijo común: `CallShield_`

Ejemplos por proceso `<proc>` (sin extensión):

- Ligero:
  - `CallShield_Light_Block_UDP_Out_<proc>`
- Estricto:
  - `CallShield_Strict_Block_UDP_Out_<proc>`
  - `CallShield_Strict_Block_TURN_TCP_<proc>` (RemotePort `3478,3479,5349,19302-19309`)
- Total:
  - `CallShield_Total_Block_All_Out_<proc>`
  - `CallShield_Total_Block_All_In_<proc>`

Reset elimina todas: `Get-NetFirewallRule -DisplayName 'CallShield_*' | Remove-NetFirewallRule`


## Inicio rápido (evitar errores comunes)

Si ves errores como **"No .NET SDKs were found"** o ejecutas comandos desde `C:\Windows\System32`, sigue estos pasos:

1. Instala **.NET 8 SDK** (no solo runtime): https://aka.ms/dotnet/download
2. Cierra y abre PowerShell de nuevo.
3. Entra al repo correcto antes de ejecutar scripts:

```powershell
cd C:\ruta\a\No-call-show
.\docs\INSTALL.ps1
```

> Si intentas ejecutar `.\docs\INSTALL.ps1` desde `C:\Windows\System32`, PowerShell no encontrará el archivo.

Comprobación rápida:

```powershell
dotnet --list-sdks
```

Debe listar al menos una versión `8.x`.

## Instalación y ejecución

### Requisitos
- Windows 10/11
- .NET 8 SDK
- PowerShell 5+ (NetSecurity)

### Build
```powershell
git clone <repo>
cd No-call-show
dotnet build .\CallShield.sln
```

### Run
```powershell
dotnet run --project .\src\CallShield.App\CallShield.App.csproj
```

La app solicitará elevación admin al aplicar/quitar reglas.

## Casos de prueba sugeridos

1. **WhatsApp Web** (modo Ligero + Chrome seleccionado)
   - Chat funciona.
   - Intento de llamada falla (remote ve no disponible/fallo conexión).
2. **Skype Web**
   - Chat funciona.
   - Llamada falla.
3. **YouTube/Netflix**
   - Navegación y reproducción normales.
4. **WebSocket típico**
   - Notificaciones siguen funcionando en modo Ligero.

## Limitaciones

- Algunas plataformas hacen fallback a TURN/TCP/443; en esos escenarios modo Ligero puede no ser suficiente.
- Modo Estricto mitiga parte del fallback pero puede afectar funciones de tiempo real.
- No se modifica permisos de mic/cam ni configuración interna de Chrome.
- Este proyecto no incluye MITM, interceptación de credenciales ni técnicas ofensivas.
