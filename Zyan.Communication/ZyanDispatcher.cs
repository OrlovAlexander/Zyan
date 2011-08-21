using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Security;
using System.Security.Principal;
using System.Transactions;
using Zyan.Communication.Delegates;
using Zyan.Communication.Notification;
using Zyan.Communication.Security;
using Zyan.Communication.SessionMgmt;
using Zyan.Communication.Toolbox;

namespace Zyan.Communication
{
	/// <summary>
	/// Central dispatch component for RPC requests.
	/// </summary>
	public class ZyanDispatcher : MarshalByRefObject, IZyanDispatcher
	{
		#region Konstruktor

		/// <summary>
		/// Konstruktor.
		/// </summary>
		/// <param name="host">Komponentenhost</param>
		public ZyanDispatcher(ZyanComponentHost host)
		{
			// Wenn kein Komponentenhost �bergeben wurde ...
			if (host == null)
				// Ausnahme werfen
				throw new ArgumentNullException("host");

			// Host �bernehmen
			_host = host;
		}

		#endregion

		#region Komponentenaufruf

		// Component Host this dispatcher is dispatching for
		private ZyanComponentHost _host = null;

		/// <summary>
		/// Creates wires between client component and server component.
		/// </summary>
		/// <param name="type">Implementation type of the server component</param>
		/// <param name="instance">Instance of the server component</param>
		/// <param name="delegateCorrelationSet">Correlation set (say how to wire)</param>
		/// <param name="wiringList">Collection of built wires</param>
		private void CreateClientServerWires(Type type, object instance, List<DelegateCorrelationInfo> delegateCorrelationSet, Dictionary<Guid, Delegate> wiringList)
		{
			if (delegateCorrelationSet == null)
				return;
			
			foreach (var correlationInfo in delegateCorrelationSet)
			{
				if (wiringList.ContainsKey(correlationInfo.CorrelationID))
					continue;

				var dynamicWire = DynamicWireFactory.CreateDynamicWire(type, correlationInfo.DelegateMemberName, correlationInfo.IsEvent);
				dynamicWire.Interceptor = correlationInfo.ClientDelegateInterceptor;

				if (correlationInfo.IsEvent)
				{
					var eventInfo = type.GetEvent(correlationInfo.DelegateMemberName);
					var dynamicEventWire = (DynamicEventWireBase)dynamicWire;

					dynamicEventWire.ServerEventInfo = eventInfo;
					dynamicEventWire.Component = instance;

					eventInfo.AddEventHandler(instance, dynamicEventWire.InDelegate);
					wiringList.Add(correlationInfo.CorrelationID, dynamicEventWire.InDelegate);
				}
				else
				{
					var outputPinMetaData = type.GetProperty(correlationInfo.DelegateMemberName);
					outputPinMetaData.SetValue(instance, dynamicWire.InDelegate, null);
					wiringList.Add(correlationInfo.CorrelationID, dynamicWire.InDelegate);
				}
			}
		}

		/// <summary>
		/// Removes wires between server and client components (as defined in correlation set).
		/// </summary>
		/// <param name="type">Type of the server component</param>
		/// <param name="instance">Instance of the server component</param>
		/// <param name="delegateCorrelationSet">Correlation set with wiring information</param>
		/// <param name="wiringList">List with known wirings</param>
		private void RemoveClientServerWires(Type type, object instance, List<DelegateCorrelationInfo> delegateCorrelationSet, Dictionary<Guid, Delegate> wiringList)
		{	
			if (delegateCorrelationSet == null)
				return;

			foreach (DelegateCorrelationInfo correlationInfo in delegateCorrelationSet)
			{
				if (correlationInfo.IsEvent)
				{
					if (wiringList.ContainsKey(correlationInfo.CorrelationID))
					{
						EventInfo eventInfo = type.GetEvent(correlationInfo.DelegateMemberName);
						Delegate dynamicWireDelegate = wiringList[correlationInfo.CorrelationID];

						eventInfo.RemoveEventHandler(instance, dynamicWireDelegate);
					}
				}
				else
				{
					PropertyInfo delegatePropInfo = type.GetProperty(correlationInfo.DelegateMemberName);
					delegatePropInfo.SetValue(instance, null, null);
				}
			}
		}

		/// <summary>
		/// Verarbeitet BeforeInvoke-Abos (falls welche registriert sind).
		/// </summary>
		/// <param name="trackingID">Aufrufschl�ssel zur Nachverfolgung</param>
		/// <param name="interfaceName">Name der Komponentenschnittstelle</param>
		/// <param name="delegateCorrelationSet">Korrelationssatz f�r die Verdrahtung bestimmter Delegaten und Ereignisse mit entfernten Methoden</param>
		/// <param name="methodName">Methodenname</param>
		/// <param name="args">Parameter</param>   
		private void ProcessBeforeInvoke(Guid trackingID, ref string interfaceName, ref List<DelegateCorrelationInfo> delegateCorrelationSet, ref string methodName, ref object[] args)
		{
			// Wenn BeforeInvoke-Abos vorhanden sind ...
			if (_host.HasBeforeInvokeSubscriptions())
			{
				// Ereignisargumente f�r BeforeInvoke erstellen
				BeforeInvokeEventArgs cancelArgs = new BeforeInvokeEventArgs()
				{
					TrackingID = trackingID,
					InterfaceName = interfaceName,
					DelegateCorrelationSet = delegateCorrelationSet,
					MethodName = methodName,
					Arguments = args,
					Cancel = false
				};
				// BeforeInvoke-Ereignis feuern
				_host.OnBeforeInvoke(cancelArgs);

				// Wenn der Aufruf abgebrochen werden soll ...
				if (cancelArgs.Cancel)
				{
					// Wenn keine Abbruchausnahme definiert ist ...
					if (cancelArgs.CancelException == null)
						// Standard-Abbruchausnahme erstellen
						cancelArgs.CancelException = new InvokeCanceledException();

					// InvokeCanceled-Ereignis feuern
					_host.OnInvokeCanceled(new InvokeCanceledEventArgs() { TrackingID = trackingID, CancelException = cancelArgs.CancelException });

					// Abbruchausnahme werfen
					throw cancelArgs.CancelException;
				}
				else // Wenn der Aufruf nicht abgebrochen werden soll ...
				{
					// Einstellungen der Ereignisargumente �bernehmen
					interfaceName = cancelArgs.InterfaceName;
					delegateCorrelationSet = cancelArgs.DelegateCorrelationSet;
					methodName = cancelArgs.MethodName;
					args = cancelArgs.Arguments;
				}
			}
		}

		/// <summary>
		/// Verarbeitet AfterInvoke-Abos (falls welche registriert sind).
		/// </summary>
		/// <param name="trackingID">Aufrufschl�ssel zur Nachverfolgung</param>
		/// <param name="interfaceName">Name der Komponentenschnittstelle</param>
		/// <param name="delegateCorrelationSet">Korrelationssatz f�r die Verdrahtung bestimmter Delegaten und Ereignisse mit entfernten Methoden</param>
		/// <param name="methodName">Methodenname</param>
		/// <param name="args">Parameter</param>   
		/// <param name="returnValue">R�ckgabewert</param>
		private void ProcessAfterInvoke(Guid trackingID, ref string interfaceName, ref List<DelegateCorrelationInfo> delegateCorrelationSet, ref string methodName, ref object[] args, ref object returnValue)
		{
			// Wenn AfterInvoke-Abos registriert sind ...
			if (_host.HasAfterInvokeSubscriptions())
			{
				// Ereignisargumente f�r AfterInvoke erstellen
				AfterInvokeEventArgs afterInvokeArgs = new AfterInvokeEventArgs()
				{
					TrackingID = trackingID,
					InterfaceName = interfaceName,
					DelegateCorrelationSet = delegateCorrelationSet,
					MethodName = methodName,
					Arguments = args,
					ReturnValue = returnValue
				};
				// AfterInvoke-Ereignis feuern
				_host.OnAfterInvoke(afterInvokeArgs);
			}
		}

		/// <summary>
		/// Gets the IP Address of the calling client from CallContext.
		/// </summary>
		/// <returns></returns>
		private IPAddress GetCallingClientIPAddress()
		{
			return CallContext.GetData("Zyan_ClientAddress") as IPAddress; ;
		}

		/// <summary>
		/// Puts the IP Address of the calling client to the current Server Session.
		/// </summary>
		private void PutClientAddressToCurrentSession()
		{
			if (ServerSession.CurrentSession == null)
				return;

			IPAddress clientAddress = GetCallingClientIPAddress();

			if (clientAddress != null)
				ServerSession.CurrentSession.ClientAddress = clientAddress.ToString();
			else
				ServerSession.CurrentSession.ClientAddress = string.Empty;
		}

		//TODO: This method needs refactoring. It�s too big.
		/// <summary>
		/// Processes remote method invocation.
		/// </summary>
		/// <param name="trackingID">Key for call tracking</param>
		/// <param name="interfaceName">Name of the component interface</param>
		/// <param name="delegateCorrelationSet">Correlation set for dynamic event and delegate wiring</param>
		/// <param name="methodName">Name of the invoked method</param>
		/// <param name="genericArguments">Generic arguments of the invoked method</param>
		/// <param name="paramTypes">Parameter types</param>
		/// <param name="args">Parameter values</param>
		/// <returns>Return value</returns>
		public object Invoke(Guid trackingID, string interfaceName, List<DelegateCorrelationInfo> delegateCorrelationSet, string methodName, Type[] genericArguments, Type[] paramTypes, params object[] args)
		{
			if (string.IsNullOrEmpty(interfaceName))
				throw new ArgumentException(LanguageResource.ArgumentException_InterfaceNameMissing, "interfaceName");

			if (string.IsNullOrEmpty(methodName))
				throw new ArgumentException(LanguageResource.ArgumentException_MethodNameMissing, "methodName");

			ProcessBeforeInvoke(trackingID, ref interfaceName, ref delegateCorrelationSet, ref methodName, ref args);

			TransactionScope scope = null;

			try
			{
				// look up the component registration info
				if (!_host.ComponentRegistry.ContainsKey(interfaceName))
				{
					throw new KeyNotFoundException(string.Format(LanguageResource.KeyNotFoundException_CannotFindComponentForInterface, interfaceName));
				}

				// check for logical context data
				var data = CallContext.GetData("__ZyanContextData_" + _host.Name) as LogicalCallContextData;
				if (data == null)
				{
					throw new SecurityException(LanguageResource.SecurityException_ContextInfoMissing);
				}

				// validate session
				var sessionID = data.Store.ContainsKey("sessionid") ? (Guid)data.Store["sessionid"] : Guid.Empty;
				if (!_host.SessionManager.ExistSession(sessionID))
				{
					throw new InvalidSessionException(string.Format(LanguageResource.InvalidSessionException_SessionIDInvalid, sessionID.ToString()));
				}

				// set current session
				var session = _host.SessionManager.GetSessionBySessionID(sessionID);
				session.Timestamp = DateTime.Now;
				ServerSession.CurrentSession = session;
				PutClientAddressToCurrentSession();

				// transfer implicit transaction
				var transaction = data.Store.ContainsKey("transaction") ? (Transaction)data.Store["transaction"] : null;
				if (transaction != null)
				{
					scope = new TransactionScope(transaction);
				}
			}
			catch (Exception ex)
			{
				_host.OnInvokeCanceled(new InvokeCanceledEventArgs
				{
					TrackingID = trackingID,
					CancelException = ex
				});

				throw ex;
			}

			// convert method arguments
			var delegateParamIndexes = new Dictionary<int, DelegateInterceptor>();
			for (int i = 0; i < paramTypes.Length; i++)
			{
				var delegateParamInterceptor = args[i] as DelegateInterceptor;
				if (delegateParamInterceptor != null)
				{
					delegateParamIndexes.Add(i, delegateParamInterceptor);
					continue;
				}

				var container = args[i] as CustomSerializationContainer;
				if (container != null)
				{
					var serializationHandler = _host.SerializationHandling[container.HandledType];
					if (serializationHandler == null)
					{
						var ex = new KeyNotFoundException(string.Format(LanguageResource.KeyNotFoundException_SerializationHandlerNotFound, container.HandledType.FullName));
						_host.OnInvokeCanceled(new InvokeCanceledEventArgs() { TrackingID = trackingID, CancelException = ex });
						throw ex;
					}

					args[i] = serializationHandler.Deserialize(container.DataType, container.Data);
				}
			}

			// get component instance
			var registration = _host.ComponentRegistry[interfaceName];
			var instance = _host.GetComponentInstance(registration);
			var type = instance.GetType();

			// wire up event handlers
			Dictionary<Guid, Delegate> wiringList = null;
			if (registration.ActivationType == ActivationType.SingleCall)
			{
				wiringList = new Dictionary<Guid, Delegate>();
				CreateClientServerWires(type, instance, delegateCorrelationSet, wiringList);
			}

			// prepare return value and invoke method
			object returnValue = null;
			bool exceptionThrown = false;

			try
			{
				var methodInfo = type.GetMethod(methodName, genericArguments, paramTypes);
				if (methodInfo == null)
				{
					var methodSignature = MessageHelpers.GetMethodSignature(type, methodName, paramTypes);
					var exceptionMessage = String.Format(LanguageResource.MissingMethodException_MethodNotFound, methodSignature);
					throw new MissingMethodException(exceptionMessage);
				}

				var serverMethodParamDefs = methodInfo.GetParameters();
				foreach (int index in delegateParamIndexes.Keys)
				{
					var delegateParamInterceptor = delegateParamIndexes[index];
					var serverMethodParamDef = serverMethodParamDefs[index];

					var dynamicWire = DynamicWireFactory.CreateDynamicWire(type, serverMethodParamDef.ParameterType);
					dynamicWire.Interceptor = delegateParamInterceptor;
					args[index] = dynamicWire.InDelegate;
				}

				returnValue = methodInfo.Invoke(instance, args, methodInfo.IsOneWay());
				if (returnValue != null)
				{
					Type returnValueType = returnValue.GetType();

					Type handledType;
					ISerializationHandler handler;
					_host.SerializationHandling.FindMatchingSerializationHandler(returnValueType, out handledType, out handler);

					if (handler != null)
					{
						byte[] raw = handler.Serialize(returnValue);
						returnValue = new CustomSerializationContainer(handledType, returnValueType, raw);
					}
				}
			}
			catch (Exception ex)
			{
				exceptionThrown = true;

				_host.OnInvokeCanceled(new InvokeCanceledEventArgs
				{
					TrackingID = trackingID,
					CancelException = ex
				});

				throw ex;
			}
			finally
			{
				if (scope != null)
				{
					if (!exceptionThrown)
						scope.Complete();

					scope.Dispose();
				}

				if (registration.ActivationType == ActivationType.SingleCall)
				{
					RemoveClientServerWires(type, instance, delegateCorrelationSet, wiringList);
					_host.ComponentCatalog.CleanUpComponentInstance(registration, instance);
				}
			}

			ProcessAfterInvoke(trackingID, ref interfaceName, ref delegateCorrelationSet, ref methodName, ref args, ref returnValue);

			return returnValue;
		}

		#endregion

		#region Ereignis-Unterst�tzung

		/// <summary>
		/// Abonniert ein Ereignis einer Serverkomponente.
		/// </summary>
		/// <param name="interfaceName">Schnittstellenname der Serverkomponente</param>
		/// <param name="correlation">Korrelationsinformation</param>
		public void AddEventHandler(string interfaceName, DelegateCorrelationInfo correlation)
		{
			// Wenn kein Schnittstellenname angegeben wurde ...
			if (string.IsNullOrEmpty(interfaceName))
				// Ausnahme werfen
				throw new ArgumentException(LanguageResource.ArgumentException_InterfaceNameMissing, "interfaceName");

			// Wenn f�r den angegebenen Schnittstellennamen keine Komponente registriert ist ...
			if (!_host.ComponentRegistry.ContainsKey(interfaceName))
				// Ausnahme erzeugen
				throw new KeyNotFoundException(string.Format("F�r die angegebene Schnittstelle '{0}' ist keine Komponente registiert.", interfaceName));

			// Komponentenregistrierung abrufen
			ComponentRegistration registration = _host.ComponentRegistry[interfaceName];

			// Wenn die Komponente nicht Singletonaktiviert ist ...
			if (registration.ActivationType != ActivationType.Singleton)
				// Prozedur abbrechen
				return;

			// Komponenteninstanz erzeugen
			object instance = _host.GetComponentInstance(registration);

			// Implementierungstyp abrufen
			Type type = instance.GetType();

			// Liste f�r �bergabe der Korrelationsinformation erzeugen
			List<DelegateCorrelationInfo> correlationSet = new List<DelegateCorrelationInfo>();
			correlationSet.Add(correlation);

			// Client- und Server-Komponente miteinander verdrahten
			CreateClientServerWires(type, instance, correlationSet, registration.EventWirings);
		}

		/// <summary>
		/// Entfernt das Abonnement eines Ereignisses einer Serverkomponente.
		/// </summary>
		/// <param name="interfaceName">Schnittstellenname der Serverkomponente</param>
		/// <param name="correlation">Korrelationsinformation</param>
		public void RemoveEventHandler(string interfaceName, DelegateCorrelationInfo correlation)
		{
			// Wenn kein Schnittstellenname angegeben wurde ...
			if (string.IsNullOrEmpty(interfaceName))
				// Ausnahme werfen
				throw new ArgumentException(LanguageResource.ArgumentException_InterfaceNameMissing, "interfaceName");

			// Wenn f�r den angegebenen Schnittstellennamen keine Komponente registriert ist ...
			if (!_host.ComponentRegistry.ContainsKey(interfaceName))
				// Ausnahme erzeugen
				throw new KeyNotFoundException(string.Format("F�r die angegebene Schnittstelle '{0}' ist keine Komponente registiert.", interfaceName));

			// Komponentenregistrierung abrufen
			ComponentRegistration registration = _host.ComponentRegistry[interfaceName];

			// Wenn die Komponente nicht Singletonaktiviert ist ...
			if (registration.ActivationType != ActivationType.Singleton)
				// Prozedur abbrechen
				return;

			// Komponenteninstanz erzeugen
			object instance = _host.GetComponentInstance(registration);

			// Implementierungstyp abrufen
			Type type = instance.GetType();

			// Liste f�r �bergabe der Korrelationsinformation erzeugen
			List<DelegateCorrelationInfo> correlationSet = new List<DelegateCorrelationInfo>();
			correlationSet.Add(correlation);

			// Client- und Server-Komponente miteinander verdrahten
			RemoveClientServerWires(type, instance, correlationSet, registration.EventWirings);
		}

		#endregion

		#region Metadaten abfragen

		/// <summary>
		/// Gibt eine Liste mit allen registrierten Komponenten zur�ck.
		/// </summary>
		/// <returns>Liste mit Namen der registrierten Komponenten</returns>
		public ComponentInfo[] GetRegisteredComponents()
		{
			// Daten vom Host abrufen
			return _host.GetRegisteredComponents().ToArray();
		}

		#endregion

		#region An- und Abmelden

		/// <summary>
		/// Meldet einen Client am Applikationserver an.
		/// </summary>
		/// <param name="sessionID">Sitzungsschl�ssel (wird vom Client erstellt)</param>
		/// <param name="credentials">Anmeldeinformationen</param>
		public void Logon(Guid sessionID, Hashtable credentials)
		{
			if (sessionID == Guid.Empty)
				throw new ArgumentException(LanguageResource.ArgumentException_EmptySessionIDIsNotAllowed, "sessionID");

			if (!_host.SessionManager.ExistSession(sessionID))
			{
				// reset current session before authentication is complete
				ServerSession.CurrentSession = null;

				AuthResponseMessage authResponse = _host.Authenticate(new AuthRequestMessage() { Credentials = credentials });
				if (!authResponse.Success)
				{
					var exception = authResponse.Exception ?? new SecurityException(authResponse.ErrorMessage);
					throw exception;
				}

				var sessionVariableAdapter = new SessionVariableAdapter(_host.SessionManager, sessionID);
				var session = new ServerSession(sessionID, authResponse.AuthenticatedIdentity, sessionVariableAdapter);
				_host.SessionManager.StoreSession(session);
				ServerSession.CurrentSession = session;

				string clientIP = string.Empty;
				IPAddress clientAddress = GetCallingClientIPAddress();

				if (clientAddress != null)
					clientIP = clientAddress.ToString();

				_host.OnClientLoggedOn(new LoginEventArgs(LoginEventType.Logon, session.Identity, clientIP, session.Timestamp));
			}
		}

		/// <summary>
		/// Meldet einen Client vom Applikationsserver ab.
		/// </summary>
		/// <param name="sessionID">Sitzungsschl�ssel</param>
		public void Logoff(Guid sessionID)
		{
			IIdentity identity = null;
			DateTime timestamp = DateTime.MinValue;

			var session = _host.SessionManager.GetSessionBySessionID(sessionID);
			if (session != null)
			{
				identity = session.Identity;
				timestamp = session.Timestamp;
			}

			// Sitzung entfernen
			_host.SessionManager.RemoveSession(sessionID);

			string clientIP = string.Empty;
			IPAddress clientAddress = GetCallingClientIPAddress();

			if (clientAddress!=null)
				clientIP=clientAddress.ToString();

			if (identity!=null)
				_host.OnClientLoggedOff(new LoginEventArgs(LoginEventType.Logoff, identity, clientIP , timestamp));

			// reset current session after the client is logged off
			ServerSession.CurrentSession = null;
		}

		#endregion

		#region Benachrichtigungen

		/// <summary>
		/// Registriert einen Client f�r den Empfang von Benachrichtigungen bei einem bestimmten Ereignis.
		/// </summary>
		/// <param name="eventName">Ereignisname</param>
		/// <param name="handler">Delegat auf Client-Ereignisprozedur</param>
		public void Subscribe(string eventName, EventHandler<NotificationEventArgs> handler)
		{
			// Wenn auf dem Host kein Benachrichtigungsdienst l�uft ...
			if (!_host.IsNotificationServiceRunning)
				// Ausnahme werfen
				throw new ApplicationException(LanguageResource.ApplicationException_NotificationServiceNotRunning);

			// F�r Benachrichtigung registrieren
			_host.NotificationService.Subscribe(eventName, handler);
		}

		/// <summary>
		/// Hebt eine Registrierung f�r den Empfang von Benachrichtigungen eines bestimmten Ereignisses auf.
		/// </summary>
		/// <param name="eventName">Ereignisname</param>
		/// <param name="handler">Delegat auf Client-Ereignisprozedur</param>
		public void Unsubscribe(string eventName, EventHandler<NotificationEventArgs> handler)
		{
			// Wenn auf dem Host kein Benachrichtigungsdienst l�uft ...
			if (!_host.IsNotificationServiceRunning)
				// Ausnahme werfen
				throw new ApplicationException(LanguageResource.ApplicationException_NotificationServiceNotRunning);

			// Registrierung aufheben
			_host.NotificationService.Unsubscribe(eventName, handler);
		}

		#endregion

		#region Sitzungsverwaltung

		/// <summary>
		/// Gibt die maximale Sitzungslebensdauer (in Minuten) zur�ck.
		/// </summary>
		public int SessionAgeLimit
		{
			get { return _host.SessionManager.SessionAgeLimit; }
		}

		/// <summary>
		/// Verl�ngert die Sitzung des Aufrufers und gibt die aktuelle Sitzungslebensdauer zur�ck.
		/// </summary>
		/// <returns>Sitzungslebensdauer (in Minuten)</returns>
		public int RenewSession()
		{
			// Kontextdaten aus dem Aufrufkontext lesen (Falls welche hinterlegt sind)
			LogicalCallContextData data = CallContext.GetData("__ZyanContextData_" + _host.Name) as LogicalCallContextData;

			// Wenn Kontextdaten �bertragen wurden ...
			if (data != null)
			{
				// Wenn ein Sitzungsschl�ssel �bertragen wurde ...
				if (data.Store.ContainsKey("sessionid"))
				{
					// Sitzungsschl�ssel lesen
					Guid sessionID = (Guid)data.Store["sessionid"];

					// Wenn eine Sitzung mit dem angegebenen Schl�ssel existiert ...
					if (_host.SessionManager.ExistSession(sessionID))
					{
						// Sitzung abrufen
						ServerSession session = _host.SessionManager.GetSessionBySessionID(sessionID);

						// Sitzung verl�ngern
						session.Timestamp = DateTime.Now;

						// Aktuelle Sitzung im Threadspeicher ablegen
						ServerSession.CurrentSession = session;
					}
					else
					{
						// Ausnahme erzeugen
						InvalidSessionException ex = new InvalidSessionException(string.Format("Sitzungsschl�ssel '{0}' ist ung�ltig! Bitte melden Sie sich erneut am Server an.", sessionID.ToString()));

						// Ausnahme werfen
						throw ex;
					}
				}
			}
			else
			{
				// Ausnahme erzeugen
				SecurityException ex = new SecurityException(LanguageResource.SecurityException_ContextInfoMissing);

				// Ausnahme werfen
				throw ex;
			}
			// Sitzungslebensdauer zur�ckgeben
			return SessionAgeLimit;
		}

		#endregion

		#region Lebenszeitsteuerung

		/// <summary>
		/// Inizialisiert die Lebenszeitsteuerung des Objekts.
		/// </summary>
		/// <returns>Lease</returns>
		public override object InitializeLifetimeService()
		{
			// Laufzeitumgebungen f�r Ereignisbasierte Komponenten leben ewig
			return null;
		}

		#endregion
	}
}