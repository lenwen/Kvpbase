﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using SyslogLogging;
using WatsonWebserver;

namespace Kvpbase
{
    public partial class StorageServer
    {
        public static HttpResponse HttpPostContainer(RequestMetadata md)
        {
            bool cleanupRequired = false; 
            ContainerSettings settings = null;

            try
            {
                #region Validate-Authentication

                if (md.User == null)
                {
                    _Logging.Log(LoggingModule.Severity.Warn, "HttpPostContainer no authentication material");
                    return new HttpResponse(md.Http, false, 401, null, "application/json",
                        new ErrorResponse(3, 401, "Unauthorized.", null), true);
                }

                #endregion
                 
                #region Check-if-Container-Exists
                 
                if (_ContainerMgr.Exists(md.User.Guid, md.Params.Container))
                {
                    _Logging.Log(LoggingModule.Severity.Warn, "HttpPostContainer container " + md.User.Guid + "/" + md.Params.Container + " already exists");
                    return new HttpResponse(md.Http, false, 409, null, "application/json",
                        new ErrorResponse(7, 409, null, null), true);
                }

                #endregion

                #region Deserialize-Request-Body

                if (md.Http.Data != null && md.Http.Data.Length > 0)
                {
                    try
                    {
                        settings = Common.DeserializeJson<ContainerSettings>(md.Http.Data);
                    }
                    catch (Exception)
                    {
                        _Logging.Log(LoggingModule.Severity.Warn, "HttpPostContainer unable to deserialize request body");
                        return new HttpResponse(md.Http, false, 400, null, "application/json",
                            new ErrorResponse(9, 400, null, null), true);
                    }
                }

                if (settings == null)
                {
                    _Logging.Log(LoggingModule.Severity.Debug, "HttpPostContainer no settings found, using defaults for " + md.User.Guid + "/" + md.Params.Container);
                    settings = new ContainerSettings(); 
                }

                #endregion

                #region Apply-Base-Settings

                settings.User = md.User.Guid.ToLower();
                settings.Name = md.Params.Container.ToLower();

                if (!String.IsNullOrEmpty(md.User.HomeDirectory)) settings.RootDirectory = md.User.HomeDirectory + settings.Name;
                else settings.RootDirectory = _Settings.Storage.Directory + settings.User + "/" + settings.Name + "/";

                settings.DatabaseFilename = settings.RootDirectory + "__Container__.db";
                settings.ObjectsDirectory = settings.RootDirectory + "__Objects__/";
                settings.HandlerType = ObjectHandlerType.Disk; 

                #endregion

                #region Create-and-Replicate

                _ContainerHandler.Create(md, settings);
                md.Http.Data = Encoding.UTF8.GetBytes(Common.SerializeJson(settings, false));

                if (!_OutboundMessageHandler.ContainerCreate(md, settings))
                {
                    _Logging.Log(LoggingModule.Severity.Warn, "HttpPostContainer unable to replicate operation to one or more nodes");
                    cleanupRequired = true;

                    return new HttpResponse(md.Http, false, 500, null, "application/json",
                        new ErrorResponse(10, 500, null, null), true);
                }

                _Logging.Log(LoggingModule.Severity.Debug, "HttpPostContainer successfully created container " + settings.User + "/" + settings.Name);
                return new HttpResponse(md.Http, true, 201, null, "application/json", null, true);

                #endregion
            }
            finally
            {
                if (cleanupRequired)
                {
                    _ContainerHandler.Delete(settings.User, settings.Name);
                    _OutboundMessageHandler.ContainerDelete(md, settings);
                }
            }
        } 
    }
}