#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <windows.h>

#include "flutter_window.h"
#include "utils.h"

int APIENTRY wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE prev,
                      _In_ wchar_t *command_line, _In_ int show_command) {
  // Attach to console when present (e.g., 'flutter run') or create a
  // new console when running with a debugger.
  if (!::AttachConsole(ATTACH_PARENT_PROCESS) && ::IsDebuggerPresent()) {
    CreateAndAttachConsole();
  }

  // Initialize COM, so that it is available for use in the library and/or
  // plugins.
  ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

  flutter::DartProject project(L"data");

  std::vector<std::string> command_line_arguments =
      GetCommandLineArguments();

  // Single Instance Check: Reuse existing window if app is already running
  HWND existing_window = ::FindWindow(L"FLUTTER_RUNNER_WIN32_WINDOW", L"jagbestuur_app");
  if (existing_window != nullptr) {
    if (!command_line_arguments.empty()) {
      const std::string& url = command_line_arguments[0];
      COPYDATASTRUCT cds;
      cds.dwData = (WM_USER + 2); // Matches app_links APPLINK_MSG_ID
      cds.cbData = static_cast<DWORD>(url.size() + 1);
      cds.lpData = const_cast<char*>(url.c_str());
      ::SendMessage(existing_window, WM_COPYDATA, 0, reinterpret_cast<LPARAM>(&cds));
    }
    ::ShowWindow(existing_window, SW_RESTORE);
    ::SetForegroundWindow(existing_window);
    ::CoUninitialize();
    return EXIT_SUCCESS;
  }

  project.set_dart_entrypoint_arguments(std::move(command_line_arguments));

  FlutterWindow window(project);
  Win32Window::Point origin(10, 10);
  Win32Window::Size size(1280, 720);
  if (!window.Create(L"jagbestuur_app", origin, size)) {
    return EXIT_FAILURE;
  }
  window.SetQuitOnClose(true);

  ::MSG msg;
  while (::GetMessage(&msg, nullptr, 0, 0)) {
    ::TranslateMessage(&msg);
    ::DispatchMessage(&msg);
  }

  ::CoUninitialize();
  return EXIT_SUCCESS;
}
