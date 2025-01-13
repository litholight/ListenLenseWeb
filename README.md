# ListenLenseWeb

ListenLenseWeb is a web application designed to facilitate the importing, reading, and audio playback of text files with synchronized sentence and paragraph highlighting. The application tracks user progress and provides features such as auto-scroll and dark mode for enhanced usability.

## Features

- **Workspace Management**: Create and manage multiple workspaces to organize your text files.
- **Text-to-Speech Integration**: Convert text files into audio using Amazon Polly.
- **Synchronized Highlighting**: Sentences and paragraphs are highlighted as the audio plays.
- **Progress Tracking**: Automatically tracks playback progress and last accessed time for each file.
- **Customizable Player**:
  - Skip backward/forward controls.
  - Adjustable playback speed.
  - Auto-scroll and dark mode toggles.

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/)
- [AWS Account](https://aws.amazon.com/) with Polly enabled.
- A supported browser for running the application.

### Setup

1. **Clone the Repository**:
   ```bash
   git clone https://github.com/your-username/ListenLenseWeb.git
   cd ListenLenseWeb
   ```

2. **Install Dependencies**:
   Restore NuGet packages:
   ```bash
   dotnet restore
   ```

3. **Configure AWS Credentials**:
   Create an `appsettings.json` file or use environment variables to configure AWS credentials:
   ```json
   {
     "AWS": {
       "AccessKey": "<your-access-key>",
       "SecretKey": "<your-secret-key>",
       "Region": "<your-region>"
     }
   }
   ```

4. **Run the Application**:
   ```bash
   dotnet run
   ```

5. **Access the Application**:
   Open your browser and navigate to `http://localhost:5066`.

## Folder Structure

```plaintext
.
├── App_Data                 # Contains workspace and progress files (excluded from Git)
├── Controllers              # MVC controllers
├── Models                   # Data models for workspace and file tracking
├── Services                 # AWS Polly and workspace services
├── Views                    # Razor views for rendering HTML
├── wwwroot                  # Static files (CSS, JS, etc.)
├── appsettings.json         # Configuration file (excluded from Git)
└── Program.cs               # Entry point of the application
```

## Usage

### Creating a Workspace
1. Navigate to the home page.
2. Click "Create Workspace" and enter a name.

### Adding a File
1. Click on an existing workspace.
2. Use the file upload form to import a `.txt` file.
3. The file will be converted to audio and a JSON file will be generated for highlighting.

### Reading and Listening
1. In a workspace, click "Read/Listen" next to a file.
2. The player will load with synchronized text highlighting and playback.
3. Use the controls to adjust playback speed, skip, toggle auto-scroll, or enable dark mode.

## Contributing

Contributions are welcome! To get started:

1. Fork the repository.
2. Create a new branch for your feature or bug fix:
   ```bash
   git checkout -b feature-name
   ```
3. Commit your changes:
   ```bash
   git commit -m "Description of changes"
   ```
4. Push to your fork and submit a pull request.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Amazon Polly](https://aws.amazon.com/polly/) for text-to-speech services.
- [Bootstrap](https://getbootstrap.com/) for frontend styling.

---

Happy listening and reading!
