# CATL - Visor de texto y logs en vivo

Aplicacion de consola en **C# / .NET 10** para Windows y Linux, ejecutable desde
PowerShell, CMD o una terminal Linux. Interfaz inspirada en Microsoft Edit, con
mouse, barras vertical y horizontal, Word Wrap y ajuste al terminal.

No abre una ventana grafica separada ni cambia el tamano de la consola.

## Ejecutar la publicacion

**PowerShell:**

```powershell
.\releases\1.0.0\win-x64\catl.exe "C:\Logs\mi aplicacion.log"
```

**CMD:**

```cmd
releases\1.0.0\win-x64\catl.exe "C:\Logs\mi aplicacion.log"
```

El ejecutable publicado es **autocontenido**: no necesita instalar .NET.
Use Windows Terminal, PowerShell o CMD en una consola interactiva.

En Linux use el binario sin extension del RID apropiado. Si lo copia desde
Windows, otorguele permiso de ejecucion con `chmod +x catl`.

> La publicacion autocontenida aun necesita las dependencias nativas del SO
> requeridas por .NET 10 y Terminal.Gui, por ejemplo ICU y ncurses en Linux.
> No admite entrada/salida redirigida ni paneles de salida de IDE.

### Argumentos

```text
catl --help       (o -h)
catl --version    (o -v)
catl -- "-archivo.log"
```

## Controles

| Control | Accion |
|---|---|
| **Ctrl-W** | Activar/desactivar Word Wrap; tambien se puede pulsar la casilla con el mouse. |
| **Ctrl-F** | Pausar/reanudar el desplazamiento automatico al final. La lectura y la informacion del archivo siguen actualizandose. Pause para revisar el historial sin que nuevos mensajes lo desplacen. |
| **Ctrl-E / boton Encoding** | Elegir Automatico, UTF-8, UTF-16 LE/BE, UTF-32 LE/BE, Windows-1252 o Latin-1. Recarga desde cero; en modo manual los UTF tambien admiten archivos sin BOM. |
| **F5** | Reabrir el mismo archivo desde cero. |
| **Ctrl-Q / boton Salir** | Cerrar el programa. |
| **Flechas, RePag/AvPag, Ctrl-Home/Ctrl-End** | Navegar por el contenido. |
| **Barras de desplazamiento** | Desplazamiento vertical/horizontal con el mouse. Con Word Wrap no hace falta desplazamiento horizontal. |

La cabecera muestra carpeta, nombre, creacion, bytes, ultima modificacion y el
encoding de lectura activo (automatico o manual).

El monitoreo consulta cada **250 ms**; la lectura no bloquea la interfaz.
La interfaz ocupa el area disponible y se adapta a cambios de ancho/alto.
La toolbar pasa a dos filas cuando hay menos de 70 columnas.
Se recomienda un minimo de **40 columnas y 16 filas**, incluido el selector.

## Formatos y codificaciones

Se admiten archivos de texto plano, por ejemplo:

```text
.txt .log .csv .tsv .json .jsonl .ndjson .xml .yaml .yml
.ini .cfg .conf .config .properties .md .sql .ps1 .bat .cmd
.cs .html .css .js .dat (solo si contiene texto)
```

Tambien archivos sin extension o con cualquier otra extension si son texto.
No se ejecutan scripts ni se interpretan los formatos: se muestra su texto.

**La extension no prueba que el contenido sea texto.** Se valida cada bloque
leido: UTF-8 estricto (incluye ASCII), UTF-8 con BOM, UTF-16 LE/BE con BOM y
UTF-32 LE/BE con BOM. Se rechazan controles salvo tabulador, CR y LF.

Se conservan caracteres Unicode aunque otro proceso los escriba por partes.
Un caracter incompleto al final queda pendiente hasta recibir el resto.

No se detecta automaticamente ANSI/Windows-1252 ni UTF-16/32 sin BOM:
seleccione el encoding manualmente con **Ctrl-E** o el boton **Encoding**.
Los logs con secuencias ANSI/ESC deben convertirse a texto sin controles.

No admite PDF, DOC/DOCX, XLS/XLSX, imagenes, ejecutables, comprimidos ni formatos
binarios, aunque se renombren a `.log` o `.txt`.

### Referencias consultadas

- [Formatos de texto - FileFormat](https://docs.fileformat.com/misc/text-formats/)
- [Extensiones comunes - FileInfo](https://fileinfo.com/filetypes/common)
- [UTF y BOM - Unicode](https://www.unicode.org/faq/utf_bom.html)

## Lectura y limitaciones

Abre con `FileShare.ReadWrite | FileShare.Delete` y solo solicita lectura.
No guarda, modifica ni bloquea deliberadamente el archivo. Si el escritor usa
acceso exclusivo, Windows puede impedir la lectura; se muestra el error y se
reintenta. Los cambios se ven cuando el escritor vacia sus buffers.

Detecta rotacion por identidad del archivo, truncamiento y reescritura del segmento
ya leido junto al final. **F5** fuerza una recarga completa. Si desaparece o falla
la lectura, conserva el ultimo contenido y muestra el error; se recupera
automaticamente cuando vuelve a estar disponible.

**Todo el contenido leido se conserva en memoria, sin recorte oculto.**
No es un visor virtualizado de archivos de varios GB: el consumo y el costo de
refrescar la vista crecen con el contenido. Las reescrituras arbitrarias de partes
anteriores del archivo requieren **F5**; el objetivo es append/tail.

El archivo puede cambiar mientras se lee; no se promete una instantanea atomica
de contenido y metadatos de un archivo escrito por otro proceso.

En Linux identifica la rotacion por dispositivo e inode. Si el sistema de archivos
no registra fecha de creacion, .NET puede mostrar una fecha alternativa.
La identidad requiere `statx` (kernel Linux **4.11 o posterior**); si un contenedor
lo bloquea mediante seccomp, se informa el error en lugar de omitir la rotacion.

## Desarrollo

Requiere el **SDK de .NET 10**.
Solucion para Visual Studio o VSCode: `src\Catl.sln`.

```powershell
dotnet build .\src\Catl.sln
dotnet run --project .\src\Catl -- "C:\Logs\aplicacion.log"
dotnet test .\src\Catl.sln
dotnet format .\src\Catl.sln --verify-no-changes
```

Los archivos de pruebas usan carpetas temporales independientes.
Dependencia de interfaz: **Terminal.Gui 1.19.0**, licencia MIT.

### Metadatos y version

Se definen en `src\Directory.Build.props`.
Cambie `<Version>` alli, con formato `mayor.menor.parche`; el ejecutable, la ayuda
de version y la carpeta de publicacion utilizan el mismo valor.

`AssemblyVersion` y `FileVersion` se derivan de `Version` por el SDK de .NET.
Autor, copyright, producto, titulo y descripcion se definen en ese archivo.

## Publicacion

**Toda la matriz de RID:**

```powershell
powershell -File .\publish.ps1
```

**Solamente un RID:**

```powershell
.\publish.ps1 -Runtime win-arm64
```

**Un subconjunto desde PowerShell:**

```powershell
.\publish.ps1 -Runtime win-x64,linux-x64,linux-arm64
```

### Ejecutables por plataforma

La estructura de salida es `releases\<version>\<RID>` con **un archivo por RID**:

| RID | Binario | Plataforma |
|---|---|---|
| `win-x64` | `catl.exe` | Windows x64 |
| `win-x86` | `catl.exe` | Windows x86 |
| `win-arm64` | `catl.exe` | Windows ARM64 |
| `linux-x64` | `catl` | Linux glibc x64 |
| `linux-arm` | `catl` | Linux glibc ARM32 (ARMv7) |
| `linux-arm64` | `catl` | Linux glibc ARM64 |
| `linux-musl-x64` | `catl` | Linux musl x64, por ejemplo Alpine |
| `linux-musl-arm` | `catl` | Linux musl ARM32 (ARMv7) |
| `linux-musl-arm64` | `catl` | Linux musl ARM64 |

No se copian DLL, PDB, README ni ZIP junto al ejecutable.

### Paquetes para GitHub Releases

Dentro de `releases\<version>` se crea un paquete por RID:

```text
catl-<version>-win-x64.zip
catl-<version>-win-x86.zip
catl-<version>-win-arm64.zip
catl-<version>-linux-x64.tar.gz
catl-<version>-linux-arm.tar.gz
catl-<version>-linux-arm64.tar.gz
catl-<version>-linux-musl-x64.tar.gz
catl-<version>-linux-musl-arm.tar.gz
catl-<version>-linux-musl-arm64.tar.gz
```

Cada paquete contiene **exclusivamente el binario**, sin una carpeta adicional.
Los TAR.GZ de Linux registran permisos **0755**, incluso al crearlos en Windows.
Estos paquetes se pueden adjuntar directamente a una version de GitHub Releases.
El empaquetador usa .NET 10; el script no requiere instalar tar, zip o 7-Zip.

### Comportamiento y compatibilidad

Las compilaciones temporales se eliminan al terminar, incluso si hay un error.
Si el destino contiene otros archivos, el script falla sin eliminarlos.

Windows ARM32 y Linux x86 no tienen runtime autocontenido oficial de .NET 10.
Las arquitecturas Linux mantenidas por terceros, como s390x o ppc64le, no tienen
los runtime packs oficiales de NuGet necesarios para este script.

Los ejecutables de otras arquitecturas se compilan en forma cruzada; su ejecucion
necesita el SO, la arquitectura y las bibliotecas correspondientes.
Los binarios y temporales de compilacion estan excluidos de Git.
