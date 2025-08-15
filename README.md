# FreqFreak
FreqFreak is a highly customizable audio visualizer for windows built on WPF. Alongside the live audio visualizer, it also includes the FVZ Tool. This can be used to pre generate visualizations for audio files, play them back, and encode them into compressed .fvz files. For more information on this, check out the [FFTVIS Repository](https://github.com/EpsiRho/FFTVIS). (This application also relies on the FFTVIS library for the FVZ Tool) For more information on the development of this project, how it works, and the FFTVIS file format, check out my [Blog Post](https://epsirho.com/posts/fft-blog) on it!

### V2 Out Now!
[Changelog](https://epsirho.com/site/project?id=FreqFreak)

## Downloads/Building
**Warning! To use per process audio (and because I don't have a PC to test with, potentially using this application at all) you must be on Windows 10 Build 20348 (aka Windows Server 2022) or higher. This came out in Late 2021, so if you've updated since then you should be fine.**

FreqFreak can be obtained from the [Microsoft Store](https://apps.microsoft.com/detail/9n44td8gbw16)! There, FreqFreak is priced at $1.99. I like to call this the "Convenience Fee", since it becomes easier to download and install, and comes with updates. Feel free to continue to download FreqFreak from the [Releases](https://github.com/EpsiRho/FreqFreak/releases) section of this GitHub as well. From GitHub, **Just unzip the release and run FreqFreak.exe**

Building the project requires [JustArion's Process Audio Capture branch for NAudio](https://github.com/JustArion/NAudio/tree/process-audio-capture), as per-process audio capture support has not been merged into NAudio yet.

## Customization
There is lots of customizable values to tune the visualization to your liking!

https://github.com/user-attachments/assets/fa2595fd-2736-4691-97c2-db4b03e98607

On startup you'll see the visuals and the Options UI. This menu can be brought up again after being closed by right clicking the gear icon in your task bar's Tray Menu (The one with the up arrow on the right that opens up to show apps that are open in the background). This icon will change to a random color on each open, to help differentiate between instances if you have multiple. The Options UI has a color picker for setting this as well.
![Options UI](https://i.imgur.com/wNbiNYR.png)
![Taskbar Tray Icon](https://i.imgur.com/PuaZwXQ.png)

From here there is so much to control!

### Bar Options
- Bar Height
	- How tall (in pixels) should the bars max height be
- Bar Height Min
	- The minimum height for all bars
- Number of Bars
	- How many bars to bin frequencies into and show
- Bar Width
	- How wide bars should be
- Bar Gap
	- The gap width in between each bar
- Peaks Mode
	- Show peak lines that hang above each bar for a specified amount of time before falling
	- Can be shown as bars and lines
- Mode
	- Bottom, Centered, Middle, Outer Ring, Inner Ring, Oscilloscope
	- How the bars should be position and extend out from
- Peak Decay
	- The decay speed (per tick) for each peak line. Only decays after a peak has hung for it's hold time
- Peak Hold
	- The time to hang a peak in the air at it's maximum seen DB level
- Show As Lines 
	- Show the visualizer bars as a line that goes through each bar's top point
- Line Thickness
	- Adjusts the line thickness for peak lines, visualizer lines, and the oscilloscope view

### Visualizer Options
- DB Floor
	- The floor to ignore sound below, a negative number. Lower lets in more sound, -70 -> -90 is typically recommended for music files.
- DB Range
	- The range of DB amplitudes being displayed. Lower values exaggerate values while higher values smooth the waveform out. 70-120 is typically normal.
- Frequency Min
	- The lowest frequency to display, typically 20hz.
- Frequency Max
	- The maximum frequency to display, typically 20000hz.
- Binning Smoothness
	- How much to smooth out peaks, by averaging +/- Smoothness bars.
- Spectrogram Mapping
	- How to map frequencies to bins.
	- Log10 will be more spectrum accurate
	- Mel will be more human hearing oriented
	- Normalized looks cooler (exaggerates and maps sections differently, see blog)
- FFT Resolution
	- The window of samples to run FFT analysis on, MUST BE A MULTIPLE OF 2
	- Typically 2048, 4096, 8192, 16384
- Attack Speed/Decay Speed
	- How fast to catch up to the current audio level. Can help smooth out the visuals, make them less jumpy/jittery
	- Attack is going up, Decay is going down.
- Invert Spectrum
	- Normal spectrum goes from Lows to Highs (20-20000 for example), this makes it backwards, high to low (20000-20)
- Rotation
	- The angle to rotate the visualizer at.
- Audio Channel
	- Selects the audio channel to display: Left, Right, Mono, or Stereo
	- Stereo supported in Centered, Circle, and Oscilloscope modes
- Bass Scaling
	- How much to scale the visualizer by when the low end is fully saturated
- Bass Shake
	- How much to shake the visualizer around by when the low end is fully saturated
- Low End Dampen
	- The amount to dampen the low end of the spectrum, slowly falls off across the spectrum from X -> 1

### Styling Options
- Bar Color Type + Peak Color Type
	- The type of color application:
	- Solid - One color
	- Dual Color Vertical - Two color gradient extending the height of the bars
	- Dual Color Horizontal - Two color gradient extending from bar to bar
	- Dual Color Height - Two color gradient based on the bar's height
	- Dual Color Pitch - Two color gradient based on the current detected pitch
	- Dual Color Frequency - Two color gradient based on the current detected peak frequency
	- Gradient Vertical - Gradient extending the height of the bars
	- Gradient Horizontal - Gradient extending from bar to bar
	- Gradient Height - Gradient color based on the bar's height
	- Gradient Pitch - Gradient based on the current detected pitch
	- Gradient Frequency - Gradient based on the current detected peak frequency
	- Each setting has different controls that will pop up for customization, Gradient modes have a custom editor that lets you make more custom gradients, including a couple presets
- Color Change Amount
	- How far to move along the color gradient each Color Change Frequency milliseconds
- Color Change Frequency
	- How fast/often to change colors
- Show Centerline
	- Shows a line through the center of the visualizer window
- Move Colors
	- None - No animation UNLESS color move and color change are not 0, then freely changes colors along the color wheel.
	- Left - Moves from color 1 to color 2 at set speed
	- Right - Moves from color 2 to color 1 at set speed
- Tray Icon Color
	- Changes the tray icons color (wont be perfectly accurate) as well as the FVZ Tool's accent color
- Always On Top
	- Decides if the visualizer should show over other windows
- Preview
	- Disables the touch target and border around the visualizer to see it without. This border + touch target disappears when exiting the options menu anyway, so this is just to preview this stat without needing to close the menu.

Additionally there are a couple buttons at the top:
- Play/Pause
	- Starts/Stops the audio thread from sending frames to the visualizer
- New Instance
	- Opens a new instance of the visualizer
- Recenter
	- Recenters the visualizer on top of the options menu
- Export
	- Export a config json file of the current settings
- Import 
	- Import a config file to load it's settings
- Audio Devices
	- Opens a selector window to choose the Output/Input/Process to visualize from
- FVZ Tool
	- Opens the FVZ Tool window. Explained more below
- Photo Cutout
	- Opens a window that lets you load and place photo cutouts that can be bass reactive. Useful for placing visualizers behind parts of your desktop background

At the top of the window you'll see a row of info:
- Render
	- The frames drawn per second
	- Typically sits around your highest monitor refresh rate, and looks stable until around 60 FPS
	- Starts to glow red when rate becomes low
- Spectra
	- The audio frames processed per second
	- Typically sits about 500-1000, any lower and it can start to feel like it's lagging behind the audio
	- Starts to glow red when rate becomes low
- Pitch/Frequency
	- Shows the current Detected Frequency Peak and Pitch
	- The longer the system is confident in a detected Frequency/Pitch the more blue the gradient behind it will become

## FVZ Tool
This tool is used to load in audio files and either load in their FVZ file, or generate one, and then play it back. You can also export generations. The settings for the visualizer are taking from the Options UI, as well some additional specific ones on the FVZ Tool window. Playback of the fvz visuals continues when scrubbing the audio timeline, as well as when you are making new generations after playback has already started. This lets you easily adjust your pre generated visualizations before saving. 

![FVZ Tool Screenshot](https://i.imgur.com/ahpF3Bg.png)

Additional settings are:
- FPS
	- The frame rate to generate at
- Audio Delay
	- The amount of delay +/- for the visuals, in case you have a delay between your PC and audio device
- Compression
	- The FVZ compression settings to use. If you're looking for "Most Compressed" enable them all, there is really not much performance/time hit. Quantization may affect visuals slightly, as it reduces their resolution (0.328546 -> 0.328 for example).
