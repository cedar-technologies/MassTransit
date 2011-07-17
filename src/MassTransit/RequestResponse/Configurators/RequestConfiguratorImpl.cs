// Copyright 2007-2011 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.RequestResponse.Configurators
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Exceptions;
	using MassTransit.Configurators;
	using Pipeline;
	using SubscriptionConnectors;


	public class RequestConfiguratorImpl<TRequest, TKey> :
		RequestConfigurator<TRequest, TKey>
		where TRequest : class, CorrelatedBy<TKey>
	{
		readonly TRequest _message;
		readonly IList<Func<IInboundPipelineConfigurator, UnsubscribeAction>> _pipelineConfigurators;
		RequestImpl<TRequest, TKey> _request;

		public RequestConfiguratorImpl(TRequest message)
		{
			_message = message;
			_pipelineConfigurators = new List<Func<IInboundPipelineConfigurator, UnsubscribeAction>>();

			_request = new RequestImpl<TRequest, TKey>(message);
		}

		public TKey CorrelationId
		{
			get { return _message.CorrelationId; }
		}

		public TRequest Request
		{
			get { return _message; }
		}

		public void Handle<TResponse>(Action<TResponse> handler)
			where TResponse : class, CorrelatedBy<TKey>
		{
			var connector = new CorrelatedHandlerSubscriptionConnector<TResponse, TKey>();

			Action<TResponse> responseHandler = message =>
				{
					try
					{
						handler(message);

						_request.Complete(message);
					}
					catch (Exception ex)
					{
						var responseException = new RequestException(message, ex);
						_request.Fail(responseException);
					}
				};

			_pipelineConfigurators.Add(x =>
				{
					return connector.Connect(x, CorrelationId, HandlerSelector.ForHandler(responseHandler));
				});
		}

		public void HandleTimeout(TimeSpan timeout, Action timeoutCallback)
		{
			_request.SetTimeout(timeout);
			_request.SetTimeoutCallback(timeoutCallback);
		}

		public void SetTimeout(TimeSpan timeout)
		{
			_request.SetTimeout(timeout);
		}

		public IEnumerable<ValidationResult> Validate()
		{
			throw new NotImplementedException();
		}

		public IRequest<TRequest, TKey> Build(IServiceBus bus)
		{
			var unsubscribeAction = bus.Configure(x =>
				{
					UnsubscribeAction unsubscribe = () => true;
					return _pipelineConfigurators.Aggregate(unsubscribe, (current, pipelineConfigurator) => current + pipelineConfigurator(x));
				});

			_request.AddCompletionCallback(() => unsubscribeAction());

			return _request;
		}

		public static IRequest<TRequest, TKey> Create<TRequest, TKey>(IServiceBus bus, TRequest message, Action<RequestConfigurator<TRequest, TKey>> configureCallback)
			where TRequest : class, CorrelatedBy<TKey>
		{
			var configurator = new RequestConfiguratorImpl<TRequest, TKey>(message);

			configureCallback(configurator);

			IRequest<TRequest, TKey> request = configurator.Build(bus);
			return request;
		}
	}
}