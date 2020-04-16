﻿using AutumnBox.Logging;
using AutumnBox.OpenFramework.Extension;
using AutumnBox.OpenFramework.Extension.Leaf;
using AutumnBox.OpenFramework.Extension.Leaf.Attributes;
using AutumnBox.OpenFramework.Open.LKit;

namespace AutumnBox.Essentials
{
    [ExtName("Example Extension")]
    [ExtDeveloperMode]
    [ExtOfficial]
    [ExtAuth("zsh2401")]
    class EExample : LeafExtensionBase
    {
        [LMain]
        public void EntryPoint(ILeafUI ui, ILogger logger)
        {
            logger.Warn("Run");
            using (ui)
            {
                ui.Title = this.GetName();
                ui.Icon = this.GetIconBytes();
                ui.Show();
                ui.WriteLine("Hello world!");
                ui.Finish();
            }
        }
    }
}
