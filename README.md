# ⚡ FullOptimizer

Optimizador de Windows 10/11 **portable** (un solo `.exe`, sin instalación) con interfaz gráfica.
Detecta el hardware del equipo y arma cada perfil según lo que tiene la PC.

## Perfiles

| Perfil | Qué hace |
|---|---|
| **Lite** | Solo limpieza: temporales, papelera, caché de Windows Update, DNS y TRIM/optimización del disco. No cambia configuraciones. |
| **Seguro** | Lite + ajustes que no quitan funciones: telemetría, anuncios, Game DVR, navegadores en segundo plano, P2P de Windows Update, menús más rápidos y compresión de memoria con poca RAM. |
| **Recomendado** | El equilibrio calculado para el equipo: SysMain off en SSD, indexación off en HDD o con poca RAM, plan de energía de alto rendimiento, MMCSS, servicios innecesarios, DISM y efectos visuales en equipos de gama baja. |
| **Extremo** | Lo máximo sin dañar nada: todo lo anterior + efectos visuales y transparencia off, Widgets/Copilot off, más servicios off, prioridad para juegos, "Máximo rendimiento" en PC de escritorio, hibernación off en escritorio y cola de impresión en manual si no hay impresoras. |

Según el hardware (gama baja / media / alta) el programa marca un perfil como **sugerido**, y cada ajuste se puede destildar antes de aplicar.

## Seguridad
- Nunca toca Windows Defender, el firewall, UAC, los drivers ni el overclock/voltajes.
- Crea un **punto de restauración** y además un **respaldo propio** de cada valor que cambia (`FullOptimizer-Respaldos`, junto al exe). El botón **Restaurar respaldo** deshace todo.
- La pestaña **Inicio de Windows** desactiva programas de arranque igual que el Administrador de tareas (reversible).

## Compilar
Requiere solo Windows (usa el compilador C# de .NET Framework 4.x que trae el sistema):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

El exe queda en `dist\FullOptimizer.exe`. El ícono se regenera con `tools\make-icon.ps1`.

## GitHub Actions
`.github/workflows/build.yml` compila en cada push a `main`/`master` y publica automáticamente
una release nueva (`v1.0.<n>`) que contiene solo `FullOptimizer.exe`. La versión también queda
grabada en el exe (título de la ventana y Propiedades > Detalles).
Para cambiar la versión mayor, editá `MAJOR_MINOR` en el workflow.
