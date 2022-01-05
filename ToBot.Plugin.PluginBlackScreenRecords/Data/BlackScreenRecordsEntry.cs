// The MIT License (MIT)
//
// Copyright (c) 2022 tariel36
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Linq;
using System.Reflection;
using ToBot.Plugin.Data;

namespace ToBot.Plugin.BlackScreenRecords.Data
{
    public class BlackScreenRecordsEntry
        : BasePluginEntry
    {
        public bool IsPreOrder { get; set; }

        public bool IsPriceRange { get; set; }

        public bool IsSoldOut { get; set; }

        public bool IsVinyl { get; set; }

        public string Title { get; set; }

        public string Price { get; set; }

        public string FullPrice { get; set; }

        public string Currency { get; set; }

        public string Url { get; set; }

        public bool SetFrom(BlackScreenRecordsEntry toProcess)
        {
            bool hasChanged = false;

            foreach (PropertyInfo property in GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => !string.Equals(x.Name, nameof(IdObject))
                    && !string.Equals(x.Name, nameof(Url))))
            {
                object oldValue = property.GetValue(this);
                object newValue = property.GetValue(toProcess);

                if (!Equals(oldValue, newValue))
                {
                    hasChanged = true;
                    property.SetValue(this, newValue);
                }
            }

            return hasChanged;
        }
    }
}
