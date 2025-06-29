# FreqFreak
FreqFreak is a highly customizable audio visualizer for windows built on WPF. Alongside the live audio visualizer, it also includes the FVZ Tool. This can be used to pre generate visualizations for audio files, play them back, and encode them into compressed .fvz files. For more information on this, check out the [FFTVIS Repository](https://github.com/EpsiRho/FFTVIS). (This application also relies on the FFTVIS library for the FVZ Tool) For more information on the development of this project, how it works, and the FFTVIS file format, check out my [Blog Post](https://epsirho.com/posts/fft-blog) on it!

## Downloads/Building
**Warning! To use per process audio (and because I don't have a PC to test with, potentially using this application at all) you must be on Windows 10 Build 20348 (aka Windows Server 2022) or higher. This came out in Late 2021, so if you've updated since then you should be fine.**

FreqFreak can be obtained from the [Releases](https://github.com/EpsiRho/FreqFreak/releases) section of this GitHub! There are x64, x86, and arm64 compilations available, as well as a framework dependent portable version that relies on .Net 9. Other variants are Self Contained (but still need the files around them). 
Just unzip the release and run FreqFreak.exe

Building the project requires [JustArion's Process Audio Capture branch for NAudio](https://github.com/JustArion/NAudio/tree/process-audio-capture), as per-process audio capture support has not been merged into NAudio yet.

## Customization
There is lots of customizable values to tune the visualization to your liking!

![Demo Gif]()

On startup you'll see the visuals and the Options UI. This menu can be brought up again after being closed by right clicking the gear icon in your task bar. This icon will change to a random color on each open, to help differentiate between instances if you have multiple. The Options UI has a color picker for setting this as well.
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
- Show Peak Lines
	- Show peak lines that hang above each bar for a specified amount of time before falling
- Position
	- Bottom, Centered, Middle, Outer Ring, Inner Ring
	- How the bars should be position and extend out from
- Peak Decay
	- The decay speed (per tick) for each peak line. Only decays after a peak has hung for it's hold time
- Peak Hold
	- The time to hang a peak in the air at it's maximum seen DB level

### Visualizer Options
- DB Floor
	- The floor to ignore sound below, a negative number. Lower lets in more sound, -70 -> -90 is typically recommended for music files.
- DB Range
	- The range of DB amplitudes being displayed. Lower values exaggerate values while higher values smooth the waveform out. 70-120 is typically normal.
- Frequency Min
	- The lowest frequency to display, typically 20hz.
- Frequency Max
	- The maximum frequency to display, typically 20000hz.
- Smoothness
	- How much to smooth out peaks, by averaging +/- Smoothness bars.
- Bin Map
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

### Styling Options
- Bar Color Type
	- Solid - One color
	- Gradient Vertical - Two color gradient extending the height of the bars
	- Gradient Horizontal - Two color gradient extending from bar to bar
	- Gradient Height - Two color gradient extending with a bar's height
- Color Move Speed
	- How far to move along the color gradient each Color Change Frequency milliseconds
- Color Change Frequency
	- How fast/often to change colors
- Move Colors
	- None - No animation UNLES color move and color change are not 0, then freely changes colors along the color wheel.
	- Left - Moves from color 1 to color 2 at set speed
	- Right - Moves from color 2 to color 1 at set speed
- Tray Icon Color
	- Changes the tray icons color (wont be perfectly accurate) as well as the FVZ Tool's accent color
- Always On Top
	- Decides if the visualizer should show over other windows
- Preview
	- Disables the touch target and border around the visualizer to see it without. This border + touch target disappears when exiting the options menu anyway, so this is just to preview this stat without needing to close the menu.

Additionally there are Export and Import Config buttons which will export/import all of these settings to/from a json file.

You can also set your Input/Output audio device to show visualizations from. With this, you can also select an app that is playing audio to get an exclusive per-process stream of audio for visualization. This allows you to host multiple instances of FreqFreak and show different visualizers for different audio sources!

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
