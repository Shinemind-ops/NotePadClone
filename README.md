# NotePad Clone
#### About
Notepad Clone is a modern reimagining of the classic Windows Notepad application, specifically tailored to mimic the Windows 11 version. This project was developed to enhance my software development skills, showcasing a blend of traditional text editing with a contemporary, user-friendly interface. Designed primarily for Windows 10 users, it offers a visually appealing alternative to the bright and simplistic native Notepad, with a dark and bright theme for a more comfortable user experience.

**!!! Project currently on hold !!!**

![image](https://github.com/thomaswening/NotePadClone/assets/25326391/d80fba4b-cfa5-4307-863b-bedfa73543e2)

#### Dependencies
- `MaterialDesignColors` (Version 2.1.4)
- `MaterialDesignThemes` (Version 4.9.0)
- `WpfEssentials`: This is a custom dependency. The repository can be found at [WpfEssentials GitHub Repo](https://github.com/thomaswening/WpfEssentials). Please follow the build instructions in the repository's README to compile the library and add the resulting DLL as a reference or include the project as a shared project in the solution.

#### Building the Project
1. **Prerequisites**:
   - .NET 8.0 SDK.
   - Visual Studio 2022 or later.

2. **Clone and Setup**:
   - Clone this repository to your local machine.
   - Clone the [WpfEssentials repository](https://github.com/thomaswening/WpfEssentials), build it, and add it to the solution as mentioned above.

3. **Restore NuGet Packages**:
   - Open the solution in Visual Studio.
   - Restore NuGet packages.

4. **Build and Run**:
   - Set NotepadClone as the startup project.
   - Build and run the application with F5 or "Start".

#### Features
- Clone of the Windows 11 Notepad for Windows 10 users.
- Toggle to switch between light and dark theme.
- Basic text editing functions: open, edit, and save documents.
- StatusBar with document statistics.
- Custom window chrome to resemble Windows 11 style.
- Developed using MVVM architecture for maintainability.
- Tabs to open several files in the same window.

#### Planned features 
- *Unit tests! Because, let's be neat and tidy.*
- More items on the menu bar, e.g. Edit, View, ...
- Editing features: Search, replacem, undo/redo, copy, cut, paste, select all, find, find next/previous.
- Functionality that keeps the last open documents if they have not yet been saved before the window is closed.
- Saving documents improvements: Autosave, warning dialog when closing documents with unsaved changes.
- Saving and opening different file types/
- Opening several documents at once.
- Status bar improvements: cursor position, zoom, line ending, encoding.
- Drag & drop for tabs.
- An icon for the whole thing - maybe a unicorn? :D
- Possibly an alternative UI based on WinUI 3.0 but I will first have to learn using that technology.
- Whatever useful comes to mind.

## License
This project is licensed under the GPL-3.0 License - see the [LICENSE](LICENSE.txt) file for details.

---

## Modifications in this fork (notepad-mods branch)

This fork adds two small quality-of-life features that the upstream project left out, both accessible from a new **View** menu:

| Feature | Behaviour |
|---|---|
| **Word wrap** (toggle, default on) | Matches Windows 11 Notepad: long lines wrap inside the editor instead of forcing horizontal scrolling. Toggleable at runtime. Implemented by binding the editor `TextBox.TextWrapping` to a new `WordWrapMode` property in `MainWindowVm`. |
| **File path bar** | A slim bar under the menu showing the full path of the current tab, with a one-click **Copy** button (copies the path to clipboard). Useful when working with files opened from scripts/downloads. Bound to `ShowPathBar` / `CopyPathCommand`. |

Maintenance notes:
- The original `WpfEssentials` package reference pointed at a repository that is now gone (404). This fork replaces it with a minimal local shim (`NotePadClone/WpfEssentialsShim.cs`) re-implementing only the two classes actually used (`ObservableObject`, `DelegateCommand`), with equivalent behaviour.
- Built and tested against .NET 8 WPF (`dotnet build -c Release`, 0 warnings / 0 errors).
- `main` branch tracks upstream; the changes live on the `notepad-mods` branch.
