// // Copyright (c) Microsoft. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Windows;

namespace StickyNotesDemo
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stick It");
            Directory.CreateDirectory(dataDirectory);
            Directory.SetCurrentDirectory(dataDirectory);
            base.OnStartup(e);
        }
    }
}
