using System.Reflection;
using System.Reflection.Emit;
using Ballware.Ml.Metadata;
using Microsoft.Extensions.Primitives;
using Microsoft.ML;
using Microsoft.ML.AutoML;
using Microsoft.ML.Data;
using Newtonsoft.Json;

namespace Ballware.Ml.Engine.AutoMl.Internal;

class AutoMlModelBuilder
{
    public ColumnInformation BuildColumnInformation(ModelMetadata metaData)
    {
        var result = new ColumnInformation();
        
        switch (metaData.Type)
        {
            case ModelTypes.Regression:
            {
                var regressionOptions = JsonConvert.DeserializeObject<RegressionOptions>(metaData.Options);
                
                foreach (var featureField in regressionOptions.FeatureFields)
                {
                    switch (featureField.Type)
                    {
                        case FieldTypes.Bool:
                            result.CategoricalColumnNames.Add(featureField.Name);
                            break;
                        case FieldTypes.Int:
                            result.NumericColumnNames.Add(featureField.Name);
                            break;
                        case FieldTypes.Decimal:
                            result.NumericColumnNames.Add(featureField.Name);
                            break;
                        case FieldTypes.Double:
                            result.NumericColumnNames.Add(featureField.Name);
                            break;
                        case FieldTypes.String:
                            result.CategoricalColumnNames.Add(featureField.Name);
                            break;
                        case FieldTypes.Date:
                        case FieldTypes.Datetime:
                        case FieldTypes.Time:
                            result.IgnoredColumnNames.Add(featureField.Name);
                            break;
                    }
                }

                result.LabelColumnName = regressionOptions.PredictionField.Name; 
            } 
            break;
        }

        return result;
    }
    
    public DataViewSchema BuildDataViewSchema(ModelMetadata metaData)
    {
        var builder = new DataViewSchema.Builder();
        
        switch (metaData.Type)
        {
            case ModelTypes.Regression:
            {
                var regressionOptions = JsonConvert.DeserializeObject<RegressionOptions>(metaData.Options);
                
                foreach (var featureField in regressionOptions.FeatureFields)
                {
                    builder.AddColumn(featureField.Name, GetDataViewPropertyType(featureField.Type));
                }

                builder.AddColumn(regressionOptions.PredictionField.Name, GetDataViewPropertyType(regressionOptions.PredictionField.Type));;
            }
            break;
        }

        return builder.ToSchema();
    }
    
    private DataViewType GetDataViewPropertyType(FieldTypes fieldType)
    {
        switch (fieldType)
        {
            case FieldTypes.Bool:
                return BooleanDataViewType.Instance;
            case FieldTypes.Int:
                return NumberDataViewType.Single;
            case FieldTypes.Decimal:
                return NumberDataViewType.Single;
            case FieldTypes.Double:
                return NumberDataViewType.Double;
            case FieldTypes.String:
                return TextDataViewType.Instance;
            case FieldTypes.Date:
            case FieldTypes.Datetime:
            case FieldTypes.Time:
                return DateTimeDataViewType.Instance;
        }

        return TextDataViewType.Instance;
    } 
    
    private Type GetPropertyType(FieldTypes fieldType)
    {
        switch (fieldType)
        {
            case FieldTypes.Bool:
                return typeof(bool);
            case FieldTypes.Int:
                return typeof(float);
            case FieldTypes.Decimal:
                return typeof(decimal);
            case FieldTypes.Double:
                return typeof(double);
            case FieldTypes.String:
                return typeof(string);
            case FieldTypes.Date:
            case FieldTypes.Datetime:
            case FieldTypes.Time:
                return typeof(DateTime);
        }

        return typeof(string);
    }

    private object ParseValue(FieldTypes fieldType, string value)
    {
        switch (fieldType)
        {
            case FieldTypes.Bool:
                return bool.Parse(value);
            case FieldTypes.Int:
                return float.Parse(value);
            case FieldTypes.Decimal:
                return decimal.Parse(value);
            case FieldTypes.Double:
                return double.Parse(value);
            case FieldTypes.String:
                return value;
            case FieldTypes.Date:
            case FieldTypes.Datetime:
            case FieldTypes.Time:
                return DateTime.Parse(value);
        }

        return value;
    }

    public object CreateInput(Type inputType, ModelMetadata metaData, IDictionary<string, object> query)
    {
        var inputInstance = inputType.GetConstructors()
            .Where(x => x.GetParameters().Length == 0)
            .First()
            .Invoke(new object[] { });
        
        var regressionOptions = JsonConvert.DeserializeObject<RegressionOptions>(metaData.Options);
                    
        foreach (var featureField in regressionOptions.FeatureFields)
        {
            if (query.TryGetValue(featureField.Name, out object value))
            {
                var property = inputType.GetProperty(featureField.Name, BindingFlags.Public | BindingFlags.Instance);

                if (property != null && property.CanWrite)
                {
                    property.SetValue(inputInstance, ParseValue(featureField.Type, value.ToString()));
                }
            }
        }

        return inputInstance;
    }
    
    public Type CompileInputType(Guid tenantId, ModelMetadata metaData)
    {
        TypeBuilder tb = GetTypeBuilder(tenantId, $"{metaData.Identifier}_Input");
        
        switch (metaData.Type)
        {
            case ModelTypes.Regression:
            {
                var regressionOptions = JsonConvert.DeserializeObject<RegressionOptions>(metaData.Options);
                    
                int loadColumnCount = 0;
                
                foreach (var featureField in regressionOptions.FeatureFields)
                {
                    CreateProperty(tb, featureField.Name, GetPropertyType(featureField.Type), new CustomAttributeBuilder[]
                    {
                        new CustomAttributeBuilder(typeof(LoadColumnAttribute).GetConstructor(new Type[] { typeof(int) }), new object[] { loadColumnCount++ }),
                        new CustomAttributeBuilder(typeof(ColumnNameAttribute).GetConstructor(new [] { typeof(string) }), new object[] { featureField.Name })
                    });
                }

                CreateProperty(tb, regressionOptions.PredictionField.Name, GetPropertyType(regressionOptions.PredictionField.Type));
            }
            break;
        }
        
        return tb.CreateType();
    }
    
    public Type CompileOutputType(Guid tenantId, ModelMetadata metaData)
    {
        TypeBuilder tb = GetTypeBuilder(tenantId, $"{metaData.Identifier}_Output");
        
        switch (metaData.Type)
        {
            case ModelTypes.Regression:
                {
                    var regressionOptions = JsonConvert.DeserializeObject<RegressionOptions>(metaData.Options);
                        
                    foreach (var featureField in regressionOptions.FeatureFields)
                    {
                        CreateProperty(tb, featureField.Name, GetPropertyType(featureField.Type), new CustomAttributeBuilder[]
                        {
                            new CustomAttributeBuilder(typeof(ColumnNameAttribute).GetConstructor(new [] { typeof(string) }), new object[] { featureField.Name })
                        });
                    }

                    CreateProperty(tb, "Features", typeof(float[]), new CustomAttributeBuilder[]
                    {
                        new CustomAttributeBuilder(typeof(ColumnNameAttribute).GetConstructor(new [] { typeof(string) }), new object[] { "Features" }),
                        new CustomAttributeBuilder(typeof(JsonIgnoreAttribute).GetConstructor(new Type[] {}), new object[] {})
                    });
                    
                    CreateProperty(tb, "Score", typeof(float), new CustomAttributeBuilder[]
                    {
                        new CustomAttributeBuilder(typeof(ColumnNameAttribute).GetConstructor(new [] { typeof(string) }), new object[] { "Score" })
                    });
                }
                break;
        }
        
        return tb.CreateType();
    }

    private TypeBuilder GetTypeBuilder(Guid tenantId, string typeName)
    {
        var typeSignature = typeName;
        var an = new AssemblyName(typeSignature);
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("Ml_" + tenantId.ToString().ToLowerInvariant());
        TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                TypeAttributes.Public |
                TypeAttributes.Class |
                TypeAttributes.AutoClass |
                TypeAttributes.AnsiClass |
                TypeAttributes.BeforeFieldInit |
                TypeAttributes.AutoLayout,
                null);
        return tb;
    }

    private void CreateProperty(TypeBuilder tb, string propertyName, Type propertyType, CustomAttributeBuilder[] customAttributes = null)
    {
        FieldBuilder fieldBuilder = tb.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);

        PropertyBuilder propertyBuilder = tb.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);
        MethodBuilder getPropMthdBldr = tb.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
        ILGenerator getIl = getPropMthdBldr.GetILGenerator();

        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, fieldBuilder);
        getIl.Emit(OpCodes.Ret);

        MethodBuilder setPropMthdBldr =
            tb.DefineMethod("set_" + propertyName,
              MethodAttributes.Public |
              MethodAttributes.SpecialName |
              MethodAttributes.HideBySig,
              null, new[] { propertyType });

        ILGenerator setIl = setPropMthdBldr.GetILGenerator();
        Label modifyProperty = setIl.DefineLabel();
        Label exitSet = setIl.DefineLabel();

        setIl.MarkLabel(modifyProperty);
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, fieldBuilder);

        setIl.Emit(OpCodes.Nop);
        setIl.MarkLabel(exitSet);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getPropMthdBldr);
        propertyBuilder.SetSetMethod(setPropMthdBldr);

        if (customAttributes != null)
        {
            foreach (var customAttribute in customAttributes)
            {
                propertyBuilder.SetCustomAttribute(customAttribute);
            }
        }
    }
}