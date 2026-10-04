#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>
#include <stdlib.h>

static int fail(const wchar_t *detail) {
    MessageBoxW(NULL, detail, L"FaultWitness — avvio non riuscito", MB_OK | MB_ICONERROR);
    return 1;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR arguments, int show) {
    (void)instance; (void)previous; (void)show;
    wchar_t directory[32768];
    DWORD length = GetModuleFileNameW(NULL, directory, 32768);
    if (!length || length >= 32768) return fail(L"Impossibile individuare la cartella del programma. Estrai completamente lo ZIP in una cartella locale e riprova.");
    wchar_t *slash = wcsrchr(directory, L'\\');
    if (!slash) return fail(L"Impossibile individuare la cartella del programma.");
    *slash = L'\0';
    wchar_t target[32768], working[32768];
    if (swprintf_s(working, 32768, L"%s\\app", directory) < 0 || swprintf_s(target, 32768, L"%s\\app\\FaultWitness.exe", directory) < 0)
        return fail(L"Il percorso della cartella è troppo lungo. Estrai lo ZIP in una cartella con un percorso più breve.");
    DWORD attributes = GetFileAttributesW(target);
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
        return fail(L"Manca app\\FaultWitness.exe. Estrai completamente lo ZIP e mantieni la cartella app accanto a questo file. Non spostare soltanto il file di avvio.");
    size_t capacity = wcslen(target) + wcslen(arguments) + 5;
    if (capacity > 32767) return fail(L"La riga di comando è troppo lunga.");
    wchar_t *command = (wchar_t *)calloc(capacity, sizeof(wchar_t));
    if (!command) return fail(L"Memoria insufficiente per avviare FaultWitness.");
    swprintf_s(command, capacity, L"\"%s\" %s", target, arguments);
    STARTUPINFOW startup = {0}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {0};
    BOOL launched = CreateProcessW(target, command, NULL, NULL, FALSE, 0, NULL, working, &startup, &process);
    DWORD error = GetLastError();
    free(command);
    if (!launched) {
        wchar_t message[512];
        swprintf_s(message, 512, L"Impossibile avviare app\\FaultWitness.exe (errore Windows %lu). Estrai completamente lo ZIP in una cartella locale. Se il problema continua, segnala questo codice insieme al messaggio di Windows.", error);
        return fail(message);
    }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    return 0;
}
