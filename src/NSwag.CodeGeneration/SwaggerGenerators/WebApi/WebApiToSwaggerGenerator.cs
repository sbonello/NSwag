﻿//-----------------------------------------------------------------------
// <copyright file="WebApiToSwaggerGenerator.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/NSwag/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NJsonSchema;
using NJsonSchema.Infrastructure;
using NSwag.CodeGeneration.Infrastructure;

namespace NSwag.CodeGeneration.SwaggerGenerators.WebApi
{
    /// <summary>Generates a <see cref="SwaggerService"/> object for the given Web API class type. </summary>
    public class WebApiToSwaggerGenerator
    {
        /// <summary>Initializes a new instance of the <see cref="WebApiToSwaggerGenerator" /> class.</summary>
        /// <param name="settings">The settings.</param>
        public WebApiToSwaggerGenerator(WebApiToSwaggerGeneratorSettings settings)
        {
            Settings = settings;
        }

        /// <summary>Gets or sets the generator settings.</summary>
        public WebApiToSwaggerGeneratorSettings Settings { get; set; }

        /// <summary>Generates a Swagger specification for the given controller type.</summary>
        /// <typeparam name="TController">The type of the controller.</typeparam>
        /// <param name="excludedMethodName">The name of the excluded method name.</param>
        /// <returns>The <see cref="SwaggerService" />.</returns>
        /// <exception cref="InvalidOperationException">The operation has more than one body parameter.</exception>
        public SwaggerService GenerateForController<TController>(string excludedMethodName = "Swagger")
        {
            return GenerateForController(typeof(TController), excludedMethodName);
        }

        /// <summary>Generates a Swagger specification for the given controller type.</summary>
        /// <param name="controllerType">The type of the controller.</param>
        /// <param name="excludedMethodName">The name of the excluded method name.</param>
        /// <returns>The <see cref="SwaggerService" />.</returns>
        /// <exception cref="InvalidOperationException">The operation has more than one body parameter.</exception>
        public SwaggerService GenerateForController(Type controllerType, string excludedMethodName = "Swagger")
        {
            var service = new SwaggerService();
            var schemaResolver = new SchemaResolver();

            GenerateForController(service, controllerType, excludedMethodName, schemaResolver);

            service.GenerateOperationIds();
            return service;
        }

        /// <summary>Generates a Swagger specification for the given controller types.</summary>
        /// <param name="controllerTypes">The types of the controller.</param>
        /// <param name="excludedMethodName">The name of the excluded method name.</param>
        /// <returns>The <see cref="SwaggerService" />.</returns>
        /// <exception cref="InvalidOperationException">The operation has more than one body parameter.</exception>
        public SwaggerService GenerateForControllers(IEnumerable<Type> controllerTypes, string excludedMethodName = "Swagger")
        {
            var service = new SwaggerService();
            var schemaResolver = new SchemaResolver();

            foreach (var controllerType in controllerTypes)
                GenerateForController(service, controllerType, excludedMethodName, schemaResolver);

            service.GenerateOperationIds();
            return service;
        }

        /// <exception cref="InvalidOperationException">The operation has more than one body parameter.</exception>
        private void GenerateForController(SwaggerService service, Type controllerType, string excludedMethodName, SchemaResolver schemaResolver)
        {
            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods.Where(m => m.Name != excludedMethodName))
            {
                var parameters = method.GetParameters().ToList();
                var methodName = method.Name;

                var operationId = methodName;
                if (operationId.EndsWith("Async"))
                    operationId = operationId.Substring(0, operationId.Length - 5);

                var operation = new SwaggerOperation();
                operation.OperationId = operationId;

                var httpPath = GetHttpPath(service, operation, controllerType, method, parameters, schemaResolver);

                LoadParameters(service, operation, parameters, schemaResolver);
                LoadReturnType(service, operation, method, schemaResolver);
                LoadMetaData(operation, method);

                foreach (var httpMethod in GetSupportedHttpMethods(method))
                {
                    if (!service.Paths.ContainsKey(httpPath))
                    {
                        var path = new SwaggerOperations();
                        service.Paths[httpPath] = path;
                    }

                    service.Paths[httpPath][httpMethod] = operation;
                }
            }

            foreach (var schema in schemaResolver.Schemes)
                service.Definitions[schema.TypeName] = schema;
        }

        private void LoadMetaData(SwaggerOperation operation, MethodInfo method)
        {
            dynamic descriptionAttribute = method.GetCustomAttributes()
                .SingleOrDefault(a => a.GetType().Name == "DescriptionAttribute");

            if (descriptionAttribute != null)
                operation.Summary = descriptionAttribute.Description;
            else
            {
                var summary = method.GetXmlDocumentation();
                if (summary != string.Empty)
                    operation.Summary = summary;
            }
        }

        private string GetHttpPath(SwaggerService service, SwaggerOperation operation, Type controllerType, MethodInfo method, List<ParameterInfo> parameters, ISchemaResolver schemaResolver)
        {
            string httpPath;

            dynamic routeAttribute = method.GetCustomAttributes()
                .SingleOrDefault(a => a.GetType().Name == "RouteAttribute");

            if (routeAttribute != null)
            {
                dynamic routePrefixAttribute = controllerType.GetCustomAttributes()
                    .SingleOrDefault(a => a.GetType().Name == "RoutePrefixAttribute");

                if (routePrefixAttribute != null)
                    httpPath = routePrefixAttribute.Prefix + "/" + routeAttribute.Template;
                else
                    httpPath = routeAttribute.Template;
            }
            else
            {
                var actionName = GetActionName(method);
                httpPath = Settings.DefaultUrlTemplate
                    .Replace("{controller}", controllerType.Name.Replace("Controller", string.Empty))
                    .Replace("{action}", actionName);
            }

            foreach (var match in Regex.Matches(httpPath, "\\{(.*?)\\}").OfType<Match>())
            {
                var parameterName = match.Groups[1].Value.Split(':').First(); // first segment is parameter name in constrained route (e.g. "[Route("users/{id:int}"]")
                var parameter = parameters.SingleOrDefault(p => p.Name == parameterName);
                if (parameter != null)
                {
                    var operationParameter = CreatePrimitiveParameter(service, parameter, schemaResolver);
                    operationParameter.Kind = SwaggerParameterKind.Path;

                    operation.Parameters.Add(operationParameter);
                    parameters.Remove(parameter);
                }
                else
                {
                    httpPath = httpPath
                        .Replace(match.Value, string.Empty)
                        .Replace("//", "/")
                        .Trim('/');
                }
            }

            return httpPath;
        }

        private string GetActionName(MethodInfo method)
        {
            dynamic actionNameAttribute = method.GetCustomAttributes()
                .SingleOrDefault(a => a.GetType().Name == "ActionNameAttribute");

            if (actionNameAttribute != null)
                return actionNameAttribute.Name;

            return method.Name;
        }

        private IEnumerable<SwaggerOperationMethod> GetSupportedHttpMethods(MethodInfo method)
        {
            // See http://www.asp.net/web-api/overview/web-api-routing-and-actions/routing-in-aspnet-web-api

            var actionName = GetActionName(method);

            var httpMethods = GetSupportedHttpMethodsFromAttributes(method).ToArray();
            foreach (var httpMethod in httpMethods)
                yield return httpMethod;

            if (httpMethods.Length == 0)
            {
                if (actionName.StartsWith("Get"))
                    yield return SwaggerOperationMethod.Get;
                else if (actionName.StartsWith("Post"))
                    yield return SwaggerOperationMethod.Post;
                else if (actionName.StartsWith("Put"))
                    yield return SwaggerOperationMethod.Put;
                else if (actionName.StartsWith("Delete"))
                    yield return SwaggerOperationMethod.Delete;
                else
                    yield return SwaggerOperationMethod.Post;
            }
        }

        private IEnumerable<SwaggerOperationMethod> GetSupportedHttpMethodsFromAttributes(MethodInfo method)
        {
            if (method.GetCustomAttributes().Any(a => a.GetType().Name == "HttpGetAttribute"))
                yield return SwaggerOperationMethod.Get;

            if (method.GetCustomAttributes().Any(a => a.GetType().Name == "HttpPostAttribute"))
                yield return SwaggerOperationMethod.Post;

            if (method.GetCustomAttributes().Any(a => a.GetType().Name == "HttpPutAttribute"))
                yield return SwaggerOperationMethod.Put;

            if (method.GetCustomAttributes().Any(a => a.GetType().Name == "HttpDeleteAttribute"))
                yield return SwaggerOperationMethod.Delete;

            if (method.GetCustomAttributes().Any(a => a.GetType().Name == "HttpOptionsAttribute"))
                yield return SwaggerOperationMethod.Options;

            dynamic acceptVerbsAttribute = method.GetCustomAttributes()
                .SingleOrDefault(a => a.GetType().Name == "AcceptVerbsAttribute");

            if (acceptVerbsAttribute != null)
            {
                foreach (var verb in ((ICollection)acceptVerbsAttribute.HttpMethods).OfType<object>().Select(v => v.ToString().ToLowerInvariant()))
                {
                    if (verb == "get")
                        yield return SwaggerOperationMethod.Get;
                    else if (verb == "post")
                        yield return SwaggerOperationMethod.Post;
                    else if (verb == "put")
                        yield return SwaggerOperationMethod.Put;
                    else if (verb == "delete")
                        yield return SwaggerOperationMethod.Delete;
                    else if (verb == "options")
                        yield return SwaggerOperationMethod.Options;
                    else if (verb == "head")
                        yield return SwaggerOperationMethod.Head;
                    else if (verb == "patch")
                        yield return SwaggerOperationMethod.Patch;
                }
            }
        }

        /// <exception cref="InvalidOperationException">The operation has more than one body parameter.</exception>
        private void LoadParameters(SwaggerService service, SwaggerOperation operation, List<ParameterInfo> parameters, ISchemaResolver schemaResolver)
        {
            foreach (var parameter in parameters)
            {
                // http://blogs.msdn.com/b/jmstall/archive/2012/04/16/how-webapi-does-parameter-binding.aspx

                var info = JsonObjectTypeDescription.FromType(parameter.ParameterType);

                dynamic fromBodyAttribute = parameter.GetCustomAttributes()
                    .SingleOrDefault(a => a.GetType().Name == "FromBodyAttribute");

                dynamic fromUriAttribute = parameter.GetCustomAttributes()
                    .SingleOrDefault(a => a.GetType().Name == "FromUriAttribute");

                // TODO: Add support for ModelBinder attribute

                if (info.IsComplexType)
                {
                    if (fromUriAttribute != null)
                        AddPrimitiveParameter(service, operation, schemaResolver, parameter);
                    else
                        AddBodyParameter(service, operation, schemaResolver, parameter);
                }
                else
                {
                    if (fromBodyAttribute != null)
                        AddBodyParameter(service, operation, schemaResolver, parameter);
                    else
                        AddPrimitiveParameter(service, operation, schemaResolver, parameter);
                }
            }

            if (operation.Parameters.Count(p => p.Kind == SwaggerParameterKind.Body) > 1)
                throw new InvalidOperationException("The operation '" + operation.OperationId + "' has more than one body parameter.");
        }

        private void AddBodyParameter(SwaggerService service, SwaggerOperation operation, ISchemaResolver schemaResolver,
            ParameterInfo parameter)
        {
            var operationParameter = CreateBodyParameter(service, parameter, schemaResolver);
            operation.Parameters.Add(operationParameter);
        }

        private void AddPrimitiveParameter(SwaggerService service, SwaggerOperation operation, ISchemaResolver schemaResolver,
            ParameterInfo parameter)
        {
            var operationParameter = CreatePrimitiveParameter(service, parameter, schemaResolver);
            operationParameter.Kind = SwaggerParameterKind.Query;
            operation.Parameters.Add(operationParameter);
        }

        private SwaggerParameter CreateBodyParameter(SwaggerService service, ParameterInfo parameter, ISchemaResolver schemaResolver)
        {
            var operationParameter = new SwaggerParameter();
            operationParameter.Schema = CreateAndAddSchema<SwaggerParameter>(service, parameter.ParameterType, schemaResolver);
            operationParameter.Name = parameter.Name;
            operationParameter.Kind = SwaggerParameterKind.Body;

            var description = parameter.GetXmlDocumentation();
            if (description != string.Empty)
                operationParameter.Description = description;

            return operationParameter;
        }

        private SwaggerParameter CreatePrimitiveParameter(SwaggerService service, ParameterInfo parameter, ISchemaResolver schemaResolver)
        {
            var parameterGenerator = new RootTypeJsonSchemaGenerator(service, Settings);

            var info = JsonObjectTypeDescription.FromType(parameter.ParameterType);
            var parameterType = info.IsComplexType ? typeof(string) : parameter.ParameterType; // complex types must be treated as string

            var segmentParameter = parameterGenerator.Generate<SwaggerParameter>(parameterType, parameter.GetCustomAttributes(), schemaResolver);
            segmentParameter.Name = parameter.Name;
            segmentParameter.Description = parameter.GetXmlDocumentation();
            return segmentParameter;
        }

        private void LoadReturnType(SwaggerService service, SwaggerOperation operation, MethodInfo method, ISchemaResolver schemaResolver)
        {
            var returnType = method.ReturnType;
            if (returnType == typeof(Task))
                returnType = typeof(void);
            else if (returnType.Name == "Task`1")
                returnType = returnType.GenericTypeArguments[0];

            var description = method.ReturnParameter.GetXmlDocumentation();
            if (description == string.Empty)
                description = null;

            var responseTypeAttributes = method.GetCustomAttributes().Where(a => a.GetType().Name == "ResponseTypeAttribute").ToList();
            if (responseTypeAttributes.Count > 0)
            {
                foreach (var responseTypeAttribute in responseTypeAttributes)
                {
                    dynamic dynResultTypeAttribute = responseTypeAttribute;
                    returnType = dynResultTypeAttribute.ResponseType;

                    var httpStatusCode = IsVoidResponse(returnType) ? "204" : "200";
                    if (responseTypeAttribute.GetType().GetRuntimeProperty("HttpStatusCode") != null)
                        httpStatusCode = dynResultTypeAttribute.HttpStatusCode;

                    var schema = CreateAndAddSchema<JsonSchema4>(service, returnType, schemaResolver);
                    operation.Responses[httpStatusCode] = new SwaggerResponse
                    {
                        Description = description,
                        Schema = schema
                    };
                }
            }
            else
            {
                if (IsVoidResponse(returnType))
                    operation.Responses["204"] = new SwaggerResponse();
                else
                {
                    var schema = CreateAndAddSchema<JsonSchema4>(service, returnType, schemaResolver);
                    operation.Responses["200"] = new SwaggerResponse
                    {
                        Description = description,
                        Schema = schema
                    };
                }
            }
        }

        private bool IsVoidResponse(Type returnType)
        {
            return returnType == null ||
                   returnType.FullName == "System.Void";
        }

        private bool IsFileResponse(Type returnType)
        {
            return returnType.Name == "IHttpActionResult" ||
                   returnType.Name == "HttpResponseMessage" ||
                   returnType.InheritsFrom("HttpResponseMessage");
        }

        private TSchemaType CreateAndAddSchema<TSchemaType>(SwaggerService service, Type type, ISchemaResolver schemaResolver)
            where TSchemaType : JsonSchema4, new()
        {
            if (type.Name == "Task`1")
                type = type.GenericTypeArguments[0];

            if (type.Name == "JsonResult`1")
                type = type.GenericTypeArguments[0];

            if (IsFileResponse(type))
                return new TSchemaType { Type = JsonObjectType.File };

            var info = JsonObjectTypeDescription.FromType(type);
            if (info.Type.HasFlag(JsonObjectType.Object))
            {
                if (type == typeof(object))
                {
                    return new TSchemaType
                    {
                        Type = JsonObjectType.Object | JsonObjectType.Null,
                        AllowAdditionalProperties = false
                    };
                }

                if (!schemaResolver.HasSchema(type, false))
                {
                    var schemaGenerator = new RootTypeJsonSchemaGenerator(service, Settings);
                    schemaGenerator.Generate<JsonSchema4>(type, schemaResolver);
                }

                return new TSchemaType
                {
                    Type = JsonObjectType.Object | JsonObjectType.Null,
                    SchemaReference = schemaResolver.GetSchema(type, false)
                };
            }

            if (info.Type.HasFlag(JsonObjectType.Array))
            {
                var itemType = type.GenericTypeArguments.Length == 0 ? type.GetElementType() : type.GenericTypeArguments[0];
                return new TSchemaType
                {
                    Type = JsonObjectType.Array | JsonObjectType.Null,
                    Item = CreateAndAddSchema<JsonSchema4>(service, itemType, schemaResolver)
                };
            }

            var generator = new RootTypeJsonSchemaGenerator(service, Settings);
            return generator.Generate<TSchemaType>(type, schemaResolver);
        }
    }
}