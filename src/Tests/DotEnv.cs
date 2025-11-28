using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

class DotEnv
{
    [ModuleInitializer]
    public static void Init()
    {
        // Load environment variables from .env files in current dir and above.
        DotNetEnv.Env.TraversePath().Load();

        // Load environment variables from user profile directory.
        var userEnv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".env");
        if (File.Exists(userEnv))
            DotNetEnv.Env.Load(userEnv);
    }
}