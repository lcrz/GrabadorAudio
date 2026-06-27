# GrabadorAudio 🎙️📸

Una potente y moderna aplicación de escritorio para Windows desarrollada en **C#** y **WPF** que permite la grabación simultánea de múltiples canales de audio y la captura rápida de pantallas y ventanas activas con teclas de acceso rápido (Hotkeys).

## Características Principales

### 🎙️ Grabación de Audio Avanzada
- **Grabación del Micrófono**: Captura el audio de cualquier micrófono o dispositivo de entrada activo conectado.
- **Grabación de Sonido del Sistema**: Captura en bucle invertido (Loopback WASAPI) todo lo que se reproduce en tu computadora sin pérdida de calidad.
- **Canales de Grabación Independientes**: Graba micrófono, audio del sistema o ambos al mismo tiempo.
- **Monitoreo en Tiempo Real**: Cuenta con barras indicadoras de volumen (Peak Meters) dinámicas para ambas fuentes de audio.
- **Mezcla y Exportación**: Combina automáticamente ambos canales de audio y exporta el archivo final en formatos populares como MP3 y WAV.

### 📸 Captura de Pantalla Integrada
- **Capturas Rápidas**: Toma capturas de pantalla completa o de ventanas activas al instante.
- **Configuración de Hotkeys**: Atajos globales configurables (por defecto: `Ctrl + Shift + S`) que funcionan incluso si la aplicación está en segundo plano o minimizada.
- **Guardado Automático**: Exporta las capturas automáticamente con marcas de tiempo legibles y etiquetas a la carpeta que desees.

### ⚙️ Gestión de Preferencias
- Panel de configuración dedicado para la selección de dispositivos de entrada predeterminados.
- Elección libre del directorio destino para las grabaciones y capturas de pantalla.
- Persistencia de configuraciones locales.

---

## Captura de Pantalla del Software
*(Aquí puedes incluir capturas de pantalla de la interfaz gráfica de usuario de GrabadorAudio)*

---

## Tecnologías Utilizadas

- **Lenguaje**: C#
- **Interfaz Gráfica**: WPF (Windows Presentation Foundation) con marcado XAML
- **Librería de Audio**: [NAudio](https://github.com/naudio/NAudio) para la captura WASAPI, codificación y mezcla de audio
- **Bibliotecas del Sistema**: Interop/PInvoke de la API Win32 de Windows para registrar atajos globales (`user32.dll` y `dwmapi.dll`)
- **Framework**: .NET

---

## Requisitos del Sistema

- **Sistema Operativo**: Windows 10 / Windows 11
- **Entorno de Ejecución**: .NET Runtime / SDK compatible con la versión definida en el proyecto.

---

## Instalación y Configuración del Desarrollo

1. **Clonar el Repositorio**:
   ```bash
   git clone git@github.com:lcrz/GrabadorAudio.git
   cd GrabadorAudio
   ```

2. **Abrir en tu IDE**:
   Abre el archivo de proyecto `GrabadorAudio.csproj` en **Visual Studio 2022** o **Rider**.

3. **Restaurar Paquetes NuGet**:
   El IDE restaurará automáticamente las dependencias necesarias como `NAudio`. En caso contrario, ejecuta:
   ```bash
   dotnet restore
   ```

4. **Compilar y Ejecutar**:
   Compila el proyecto en modo `Debug` o `Release` y ejecútalo.

---

## Licencia

Este proyecto está bajo la licencia que decida el autor. Consulta los términos detallados en el repositorio.
