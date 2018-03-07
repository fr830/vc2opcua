﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Diagnostics;
using System.ComponentModel.Composition;
using System.Collections.ObjectModel;
using Caliburn.Micro;
using VisualComponents.Create3D;
using VisualComponents.UX.Shared;

namespace vc2opcua
{
    class VcComponent
    {
        [Import]
        ISimComponent component = null;

        VcUtils _vcutils = new VcUtils();

        public VcComponent(ISimComponent inputcomponent)
        {
            component = inputcomponent;
        }

        #region Methods

        public IProperty GetProperty(string name)
        {
            foreach (IProperty property in component.Properties)
            {
                if (property.Name == name)
                {
                    return property;
                }
            }

            // If we go through all the properties and we do not find any match
            string message = String.Format("Property {0} not found", name);
            _vcutils.VcWriteWarningMsg(message);

            return null;
        }

        public void SetPropertyValueDouble(IProperty property, double value)
        {
            property.Value = value;

            component.Rebuild();
        }

        public void PrintProperties()
        {
            Debug.WriteLine("Properties: ");
            foreach (IProperty property in component.Properties)
            {
                Debug.WriteLine("  " + property.Name);
            }
        }

        #endregion
    }
}