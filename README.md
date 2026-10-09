# SchoolGuard v0.2

Bloqueo de sitios web por software para PCs de colegios (Windows). Sin hardware.

## Cómo funciona
- **SchoolGuardAgent** (servicio de Windows, arranca con la PC): DNS local en `127.0.0.1:53`.
  Los dominios bloqueados (y todos sus subdominios) devuelven NXDOMAIN; el resto se reenvía al DNS real.
  Además fuerza el DNS de la PC a `127.0.0.1` cada minuto.
- **Panel** en `http://127.0.0.1:8787` (solo desde esa PC), con login: cargar URLs, palabras clave, ver bloqueos.
  Al agregar una URL se normaliza al dominio madre y se verifica que el sitio responda.
- **SchoolGuardUpdater** (servicio aparte): consulta GitHub Releases, descarga `agent-win-x64.zip`,
  verifica SHA-256, reemplaza el agente, comprueba que arranque y, si falla, vuelve a la versión anterior.
- Datos y config en `C:\ProgramData\SchoolGuard\` (no se pierden al actualizar).

## Puesta en marcha
1. Instalá el SDK de .NET 8. Creá un repositorio **público** en GitHub y subí esta carpeta.
2. Cambiá `UpdateRepo` en `src/SchoolGuard.Common/AppConfig.cs` por `tu_usuario/tu_repo`
   (también se puede editar en `C:\ProgramData\SchoolGuard\config.json` en cada PC).
3. Publicá una versión: `git tag v0.1.0` y `git push --tags`. GitHub Actions compila todo y crea el Release
   con el instalador `SchoolGuard-Setup-0.1.0.exe`, el zip del agente y `SHA256SUMS.txt`.
4. En cada PC: ejecutar el instalador como administrador, abrir el panel, crear el administrador y cargar URLs.
5. Para actualizar: subí el número (`v0.2.0`), `git tag` + `git push --tags`. Las PCs se actualizan solas.


## Novedades de la v0.2
- **Categorías**: importá listas públicas (hosts, adblock o un dominio por línea) desde el panel; se activan/desactivan y se actualizan reimportando. Botones rápidos: Adultos, Redes sociales, Apuestas.
- **Lista blanca**: un sitio permitido nunca se bloquea (gana sobre categorías y palabras clave).
- **Anti-evasión** (solo con `EnforceDns: true`):
  - Se bloquean nombres de servidores DNS-sobre-HTTPS conocidos y el "canario" de Firefox.
  - Políticas de Windows desactivan DoH en Chrome, Edge, Brave y Firefox.
  - Reglas de firewall bloquean DNS (puerto 53) hacia cualquier servidor que no sea `UpstreamDns`, y DNS-sobre-TLS (853). Solo IPv4.
- **Carpeta de datos protegida**: solo SYSTEM y Administradores pueden leer o editar listas, claves y configuración.
- **Desinstalación autorizada**: el desinstalador solo avanza si el administrador pulsó "Autorizar desinstalación" en el panel (vence a los 30 minutos).
- Nuevas opciones en `config.json`: `BlockDohHosts`, `BlockExternalDns`, `HardenDataDir` (todas `true` por defecto).

### Qué se actualiza solo y qué no
- **Se actualiza solo (agente)**: todo el código del agente y el panel, el modo estricto, firewall, políticas de navegador y protección de datos.
- **Necesita el instalador nuevo una vez por PC**: la autorización de desinstalación y la limpieza de reglas/políticas al desinstalar (viven en el desinstalador que se crea al instalar).
- Si en `UpstreamDns` usás el DNS del router u otro distinto de 8.8.8.8, configurarlo en `config.json` ANTES de activar `EnforceDns`, porque el firewall solo deja pasar ese servidor.

## Probar en desarrollo (¡hacelo en una máquina virtual primero!)
Poné `"EnforceDns": false` en `config.json`, abrí una terminal **como administrador** y corré
`dotnet run --project src/SchoolGuard.Agent`. Probá con `nslookup dominio.com 127.0.0.1`.
Si activás `EnforceDns` y el agente no está corriendo, la PC se queda sin resolver nombres.
Para revertir: `Set-DnsClientServerAddress -InterfaceIndex <n> -ResetServerAddresses`.

## Límites conocidos de la v0.1
- Alumnos con permisos de administrador pueden detener los servicios o cambiar el DNS. Usar cuentas estándar.
- VPN y proxies web siguen pudiendo esquivar el filtro. IPv6 no está cubierto.
- Falta DNS por TCP (respuestas truncadas) y IPv6.
- Si el agente está caído durante una actualización, el updater tampoco puede descargar (no hay DNS). El rollback lo contempla, pero conviene probarlo.
- El updater no se actualiza a sí mismo (es chico y estable); si cambia, se reinstala con el instalador.
- Las palabras clave solo actúan sobre nombres de dominio, no sobre lo que se escribe en buscadores (v0.3, extensión de navegador).
