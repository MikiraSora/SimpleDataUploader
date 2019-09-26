using Sync.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleDataUploader
{
    public class Setting : IConfigurable
    {
        public ConfigurationElement UploadUrl { get; set; } = "";

        public void onConfigurationLoad()
        {
        }

        public void onConfigurationReload()
        {
        }

        public void onConfigurationSave()
        {
        }
    }
}
