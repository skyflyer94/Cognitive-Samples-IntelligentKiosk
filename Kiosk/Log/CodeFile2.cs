using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System;
using Windows.Storage;
/// <summary> 
/// This is an advanced usage, where you want to intercept the logging messages and devert them somewhere 
/// besides ETW. 
/// </summary> 
sealed class StorageFileEventListener : EventListener
{
    /// <summary> 
    /// Storage file to be used to write logs 
    /// </summary> 
    private StorageFile m_StorageFile = null;

    /// <summary> 
    /// Name of the current event listener 
    /// </summary> 
    private string m_Name;

    public StorageFileEventListener(string name)
    {
        this.m_Name = name;

        Debug.WriteLine("StorageFileEventListener for {0} has name {1}", GetHashCode(), name);

        AssignLocalFile();
    }

    private async void AssignLocalFile()
    {
        m_StorageFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("Cognitive.log",
                                                                                  CreationCollisionOption.OpenIfExists);
    }

    private async void WriteToFile(string line)
    {
        // TODO: 
        m_StorageFile = await ApplicationData.Current.LocalFolder.GetFileAsync("Cognitive.log");
        await FileIO.WriteTextAsync(m_StorageFile, line);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // TODO: 
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // TODO: 
    }
}