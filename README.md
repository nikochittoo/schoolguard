# SchoolGuard v0.1

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

## Probar en desarrollo (¡hacelo en una máquina virtual primero!)
Poné `"EnforceDns": false` en `config.json`, abrí una terminal **como administrador** y corré
`dotnet run --project src/SchoolGuard.Agent`. Probá con `nslookup dominio.com 127.0.0.1`.
Si activás `EnforceDns` y el agente no está corriendo, la PC se queda sin resolver nombres.
Para revertir: `Set-DnsClientServerAddress -InterfaceIndex <n> -ResetServerAddresses`.

## Límites conocidos de la v0.1
- Alumnos con permisos de administrador pueden detener los servicios o cambiar el DNS. Usar cuentas estándar.
- VPN, proxies web y DNS-sobre-HTTPS del navegador pueden esquivar el filtro (se cierra en v0.2: reglas de firewall / política de navegador).
- Falta DNS por TCP (respuestas truncadas) y IPv6.
- La desinstalación aún no pide clave de administrador.
- Si el agente está caído durante una actualización, el updater tampoco puede descargar (no hay DNS). El rollback lo contempla, pero conviene probarlo.
- El updater no se actualiza a sí mismo (es chico y estable); si cambia, se reinstala con el instalador.
- Las palabras clave solo actúan sobre nombres de dominio, no sobre lo que se escribe en buscadores (v0.3, extensión de navegador).
