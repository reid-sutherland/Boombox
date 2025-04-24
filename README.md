
# EXILED Example Plugin


## Note about `git`

Do not clone this repo, it should not change and should only server as an example.

Instead, download the source code .zip and extract to a folder in `C:\\Users\\<user>\\source\\repos`.

Then once you've made it your own, use git commands or SourceTree to initialize it as a new repository and upload it to GitHub.



## First time setup

### Create a server for testing

Follow this guide: https://steamcommunity.com/sharedfiles/filedetails/?id=1940790742

Notes:
- For the server location, you can just create a folder in your desktop for easy access.
- You don't have to do port forwarding if you're just testing by yourself.
  - Just use localhost (127.0.0.1) for direct connect to local server.
- You don't have to get your server verified or do any of the pastebin stuff if you don't care about it appearing in the game's server list.

### Set up environment variable for EXILED_REFERENCES

EXILED and Exiled Plugins need to reference the .dlls used by the SCP Server. 
When you set up a server in the previous section, all of these .dlls are installed automatically, we just need to be able to reference them from the `.csproj` file.

To do this, simply create a User Environment Variable for your User with the following values:
- Name: `EXILED_REFRENCES`
- Value: `<path-to-SCPServer-folder>\SCPSL_Data\Managed`

Double check that that folder exists and contains a bunch of Unity/CSharp .dlls.

#### How to set a Windows environment variable for noobs

- Search `environment variable` in Windows.
- Click `Edit the System Environment Variables`.
- Click the `Environment Variables...` button.
- Under `User variables for <user>`, click `New...`.
- Use the Name and Value listed above. You can use `Browse Directory...` to find the directory manually if you don't want to type the full path.
- Click `Apply` or `OK` until all the windows are gone.

### Get a publicized Assembly dll for CSharp

Not all plugins will need this, but many (such as Common Utilities) will.

If your project requires `Assembly-CSharp-Publicized.dll`, follow these steps:
- Go to this github page: https://github.com/Raul125/APublicizer/releases/tag/1.0.0
- Download `Release.rar`.
- Use 7-zip (or WinRAR LMAO) to extract it.
- Open a Command Prompt and navigate to the extracted folder.
- Run this command: `APublicizer.exe <SCPServer_location>\SCPSL_Data\Managed\Assembly-CSharp.dll`
- This will publicize the `Assembly-CSharp.dll` and save it to the same .dll folder with the name `Assembly-CSharp-Publicized.dll`.
- You can delete the `APublicizer` folder and contents now, you will likely never need it again :)



## Live Debugging

### Setup

Make a copy of your server in case you mess something up.

- Check if you Visual Studio / IDE as the extension for Unity Dev. If not, install it.
- Download the Unity 64-bit, version: 2021.3.17f1.
  - Unpack the executable using Winrar or 7zip.
  - `[ExtractedFolder]\Editor\Data\PlaybackEngines\windowsstandalonesupport\Variations\win64_development_mono\`
  - Take `UnityPlayer.dll` and replace the one found at the root of the server.
  - Take `WindowsPlayer.exe` place it at the root of server, delete `SCPSL.exe`, rename `WindowsPlayer.exe` to `SCPSL.exe`.
  - You can delete UnitySetup64-2021.3.17f1.exe and the extracted folder.
- Go to `[Server root]\SCPSL_Data\boot.config`. Edit and add the following:
  - ```
    wait-for-managed-debugger=1
    player-connection-debug=1
    ```

### Debug

- Place break point in your code. 
- Build and place the dll in server ... 
- Start the server... A windows will open which will suggest you attach your IDE. Go to or IDE and attached it.
- For Visual Studio (min 2019):
  - Go to Debugging > Attach to Unity > Select Project call "Debug" > click Ok.
  - Go to the windows open by the server and click Ok.
  - Disable the auto crash restart: `HBCTRL disable`
- When a break point is triggered, you can inspect the value of instant.

### Disable Debugging

- Go to `[Server root]\SCPSL_Data\boot.config`. Modify the lines from before to the following:
  - ```
    wait-for-managed-debugger=0
    player-connection-debug=0
    ```