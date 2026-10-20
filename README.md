# WebViewC-

## Windows x64 단일 파일 배포

Visual Studio에서 `LineMonitor.App` 프로젝트를 우클릭하고 **게시 → SingleFileWin64 → 게시**를 실행합니다.
일반 빌드가 아니라 게시를 실행해야 단일 실행 파일이 생성됩니다.

명령줄에서는 저장소 루트에서 다음 명령을 실행합니다.

```powershell
dotnet publish LineMonitor/LineMonitor.App/LineMonitor.App.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=SingleFileWin64
```

결과 폴더는 `LineMonitor/실행파일/`입니다.
`LineMonitor.App.exe`와 별도 설정 파일인 `appsettings.json`을 같은 폴더에 두고 배포합니다.
DB 접속 설정은 실행 파일 옆의 `appsettings.json`에서 수정합니다.

.NET 런타임도 EXE에 포함됩니다. 네이티브 DLL은 EXE에 묶이며 실행 시 Windows 임시 폴더에 자동으로 추출됩니다.
자세한 동작은 [Microsoft 단일 파일 배포 문서](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)를 참고하세요.
