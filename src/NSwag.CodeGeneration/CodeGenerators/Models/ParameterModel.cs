//-----------------------------------------------------------------------
// <copyright file="ParameterModel.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/NSwag/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using NJsonSchema;

namespace NSwag.CodeGeneration.CodeGenerators.Models
{
    internal class ParameterModel
    {
        public string Name { get; set; }

        public string VariableNameLower { get; set; }

        public SwaggerParameterKind Kind { get; set; }

        public string Type { get; set; }

        public string Description { get; set; }

        public bool HasDescription
        {
            get { return !string.IsNullOrEmpty(Description); }
        }
        
        public bool HasDescriptionOrIsOptional
        {
            get { return HasDescription || IsOptional; }
        }

        public bool IsLast { get; set; }

        public bool IsDate
        {
            get { return Schema.Type == JsonObjectType.String && Schema.Format == JsonFormatStrings.DateTime; }
        }

        public bool IsArray
        {
            get { return Schema.Type == JsonObjectType.Array; }
        }

        public bool IsOptional { get; set; }

        public JsonSchema4 Schema { get; set; }
    }
}