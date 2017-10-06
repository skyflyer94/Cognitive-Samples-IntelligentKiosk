using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System;
using Windows.Storage;
/// <summary> 
/// This is an advanced usage, where you want to intercept the logging messages and devert them somewhere 
/// besides ETW. 
/// </summary> 
sealed class FileLog : EventListener
{
    /// <summary> 
    /// Storage file to be used to write logs 
    /// </summary> 
    private StorageFile m_StorageFile = null;

    /// <summary> 
    /// Name of the current event listener 
    /// </summary> 
    private string m_Name;

    public FileLog(string name)
    {
        this.m_Name = name;

        AssignLocalFile();
    }

    private async void AssignLocalFile()
    {
        m_StorageFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(m_Name.Replace(" ", "_") + "log",
                                                                                  CreationCollisionOption.OpenIfExists);
        //Debug.Write(m_Name.Replace(" ", "_") + "log");
        //Debug.Write(ApplicationData.Current.LocalFolder.Path);
    }

    public async void WriteToFile(string line)
    {
        // TODO: 
        m_StorageFile = await ApplicationData.Current.LocalFolder.GetFileAsync(m_Name.Replace(" ", "_") + "log");
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