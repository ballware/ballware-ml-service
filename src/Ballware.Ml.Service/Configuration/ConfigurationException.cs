namespace Ballware.ML.Service.Configuration;

public class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);