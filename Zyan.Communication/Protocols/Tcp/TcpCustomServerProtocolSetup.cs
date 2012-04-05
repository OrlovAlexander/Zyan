﻿using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using Zyan.Communication.ChannelSinks.Encryption;
using Zyan.Communication.Security;
using Zyan.Communication.Toolbox;
using System.Collections;

namespace Zyan.Communication.Protocols.Tcp
{
	/// <summary>
	/// Server protocol setup for TCP communication with support for user defined authentication and security.
	/// </summary>
	public class TcpCustomServerProtocolSetup : ServerProtocolSetup
	{
		private bool _encryption = true;
		private string _algorithm = "3DES";
		private bool _oaep = false;
		private int _tcpPort = 0;

		/// <summary>
		/// Gets or sets the TCP port to listen for client calls.
		/// </summary>
		public int TcpPort
		{
			get { return _tcpPort; }
			set
			{
				if (_tcpPort < 0 || _tcpPort > 65535)
					throw new ArgumentOutOfRangeException("tcpPort", LanguageResource.ArgumentOutOfRangeException_InvalidTcpPortRange);

				_tcpPort = value;
			}
		}

		/// <summary>
		/// Gets or sets the name of the symmetric encryption algorithm.
		/// </summary>
		public string Algorithm
		{
			get { return _algorithm; }
			set { _algorithm = value; }
		}

		/// <summary>
		/// Gets or sets, if OEAP padding should be activated.
		/// </summary>
		public bool Oeap
		{
			get { return _oaep; }
			set { _oaep = value; }
		}

		/// <summary>
		/// Gets or sets, if socket caching is enabled.
		/// </summary>
		public bool SocketCachingEnabled
		{ get; set; }

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="versioning">Versioning behavior</param>
		public TcpCustomServerProtocolSetup(Versioning versioning)
			: base((settings, clientSinkChain, serverSinkChain) => new TcpChannel(settings, clientSinkChain, serverSinkChain))
		{
			SocketCachingEnabled = true;
			_channelName = "TcpCustomServerProtocolSetup_" + Guid.NewGuid().ToString();
			_versioning = versioning;

			Hashtable formatterSettings = new Hashtable();
			formatterSettings.Add("includeVersions", _versioning == Versioning.Strict);
			formatterSettings.Add("strictBinding", _versioning == Versioning.Strict);

			ClientSinkChain.Add(new BinaryClientFormatterSinkProvider(formatterSettings, null));
			ServerSinkChain.Add(new BinaryServerFormatterSinkProvider(formatterSettings, null) { TypeFilterLevel = TypeFilterLevel.Full });
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		public TcpCustomServerProtocolSetup()
			: this(Versioning.Strict)
		{ }

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		public TcpCustomServerProtocolSetup(int tcpPort, IAuthenticationProvider authProvider)
			: this()
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="versioning">Versioning behavior</param>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		public TcpCustomServerProtocolSetup(Versioning versioning, int tcpPort, IAuthenticationProvider authProvider)
			: this(versioning)
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		public TcpCustomServerProtocolSetup(int tcpPort, IAuthenticationProvider authProvider, bool encryption)
			: this()
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="versioning">Versioning behavior</param>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		public TcpCustomServerProtocolSetup(Versioning versioning, int tcpPort, IAuthenticationProvider authProvider, bool encryption)
			: this(versioning)
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		/// <param name="algorithm">Encryption algorithm (e.G. "3DES")</param>
		public TcpCustomServerProtocolSetup(int tcpPort, IAuthenticationProvider authProvider, bool encryption, string algorithm)
			: this()
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
			_algorithm = algorithm;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="versioning">Versioning behavior</param>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		/// <param name="algorithm">Encryption algorithm (e.G. "3DES")</param>
		public TcpCustomServerProtocolSetup(Versioning versioning, int tcpPort, IAuthenticationProvider authProvider, bool encryption, string algorithm)
			: this(versioning)
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
			_algorithm = algorithm;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		/// <param name="algorithm">Encryption algorithm (e.G. "3DES")</param>
		/// <param name="oaep">Specifies if OAEP padding should be used</param>
		public TcpCustomServerProtocolSetup(int tcpPort, IAuthenticationProvider authProvider, bool encryption, string algorithm, bool oaep)
			: this()
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
			_algorithm = algorithm;
			_oaep = oaep;
		}

		/// <summary>
		/// Creates a new instance of the TcpCustomServerProtocolSetup class.
		/// </summary>
		/// <param name="versioning">Versioning behavior</param>
		/// <param name="tcpPort">TCP port number</param>
		/// <param name="authProvider">Authentication provider</param>
		/// <param name="encryption">Specifies if the communication sould be encrypted</param>
		/// <param name="algorithm">Encryption algorithm (e.G. "3DES")</param>
		/// <param name="oaep">Specifies if OAEP padding should be used</param>
		public TcpCustomServerProtocolSetup(Versioning versioning, int tcpPort, IAuthenticationProvider authProvider, bool encryption, string algorithm, bool oaep)
			: this(versioning)
		{
			TcpPort = tcpPort;
			AuthenticationProvider = authProvider;
			_encryption = encryption;
			_algorithm = algorithm;
			_oaep = oaep;
		}

		private bool _encryptionConfigured = false;

		/// <summary>
		/// Configures encrpytion sinks, if encryption is enabled.
		/// </summary>
		private void ConfigureEncryption()
		{
			if (_encryption)
			{
				if (_encryptionConfigured)
					return;

				_encryptionConfigured = true;

				this.AddClientSinkAfterFormatter(new CryptoClientChannelSinkProvider()
				{
					Algorithm = _algorithm,
					Oaep = _oaep
				});
				this.AddServerSinkBeforeFormatter(new CryptoServerChannelSinkProvider()
				{
					Algorithm = _algorithm,
					RequireCryptoClient = true,
					Oaep = _oaep
				});
			}
		}

		/// <summary>
		/// Creates and configures a Remoting channel.
		/// </summary>
		/// <returns>Remoting channel</returns>
		public override IChannel CreateChannel()
		{
			IChannel channel = ChannelServices.GetChannel(_channelName);

			if (channel == null)
			{
				_channelSettings["name"] = _channelName;
				_channelSettings["port"] = _tcpPort;
				_channelSettings["socketCacheTimeout"] = 0;
				_channelSettings["socketCachePolicy"] = SocketCachingEnabled ? SocketCachePolicy.Default : SocketCachePolicy.AbsoluteTimeout;
				_channelSettings["secure"] = false;

				ConfigureEncryption();

				if (_channelFactory == null)
					throw new ApplicationException(LanguageResource.ApplicationException_NoChannelFactorySpecified);

				channel = _channelFactory(_channelSettings, BuildClientSinkChain(), BuildServerSinkChain());

				if (!MonoCheck.IsRunningOnMono)
				{
					if (RemotingConfiguration.CustomErrorsMode != CustomErrorsModes.Off)
						RemotingConfiguration.CustomErrorsMode = CustomErrorsModes.Off;
				}
				return channel;
			}
			return channel;
		}

		/// <summary>
		/// Gets or sets the authentication provider.
		/// </summary>
		public override IAuthenticationProvider AuthenticationProvider
		{
			get
			{
				return _authProvider;
			}
			set
			{
				if (value == null)
					_authProvider = new NullAuthenticationProvider();
				else
					_authProvider = value;
			}
		}

		#region Versioning settings

		private Versioning _versioning = Versioning.Strict;

		/// <summary>
		/// Gets or sets the versioning behavior.
		/// </summary>
		private Versioning Versioning
		{
			get { return _versioning; }
		}

		#endregion
	}
}